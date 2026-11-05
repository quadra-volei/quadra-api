using Quadra.Modules.InGame.Application;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Api.Background;

/// <summary>
/// Closes matches nobody closed (DECISIONS #103). A match still neither cancelled nor summarized
/// <c>Matches:AbandonAfterHours</c> (default 12) after its time is closed on the organizer's behalf:
/// never started → cancelled (a recurring group still gets its next game, #89); started → the game
/// is ended, the MVP voting closed and the summary generated (which also schedules the next game,
/// #64). Without it a forgotten game stayed "rolando agora" for days and its group stopped.
/// <c>Matches:AbandonSweepMinutes</c> (default 15) = 0 turns it off.
/// </summary>
public sealed class AbandonedMatchSweeper : BackgroundService
{
    public const string IntervalSettingKey = "Matches:AbandonSweepMinutes";
    public const string AfterHoursSettingKey = "Matches:AbandonAfterHours";
    public const int DefaultIntervalMinutes = 15;
    public const int DefaultAfterHours = 12;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AbandonedMatchSweeper> _logger;

    public AbandonedMatchSweeper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<AbandonedMatchSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = _configuration.GetValue(IntervalSettingKey, DefaultIntervalMinutes);
        if (minutes <= 0)
        {
            _logger.LogInformation("Abandoned match sweep is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    /// <summary>One pass: closes every abandoned match found. Public for the integration tests.</summary>
    public async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        var afterHours = _configuration.GetValue(AfterHoursSettingKey, DefaultAfterHours);
        IReadOnlyList<Modules.Matches.Entities.Match> matches;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            matches = await scope.ServiceProvider.GetRequiredService<IMatchRepository>()
                .ListAbandonedAsync(_timeProvider.GetUtcNow().AddHours(-afterHours), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abandoned match sweep failed; it will run again on the next tick.");
            return;
        }

        foreach (var match in matches)
        {
            try
            {
                // A fresh scope per match: one failure leaves no half-tracked state for the next.
                await using var scope = _scopeFactory.CreateAsyncScope();
                await CloseAsync(scope.ServiceProvider, match.Id, match.OrganizerId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One match that cannot be closed must not stop the others; it is retried next tick.
                _logger.LogError(ex, "Could not close abandoned match {MatchId}.", match.Id);
            }
        }
    }

    private async Task CloseAsync(IServiceProvider services, Guid matchId, Guid organizerId, CancellationToken cancellationToken)
    {
        var results = services.GetRequiredService<IMatchResultReader>();
        var game = await results.GetMatchResultAsync(matchId, cancellationToken);

        if (game is null || game.ScoreboardState == "NotStarted")
        {
            await services.GetRequiredService<CancelMatchHandler>()
                .HandleAsync(matchId, organizerId, endGroup: false, cancellationToken);
            _logger.LogInformation("Abandoned match {MatchId} never started: cancelled.", matchId);
            return;
        }

        if (game.ScoreboardState == "InProgress")
        {
            await services.GetRequiredService<EndScoreboardHandler>().HandleAsync(matchId, organizerId, cancellationToken);
            game = await results.GetMatchResultAsync(matchId, cancellationToken);
        }

        if (game?.MvpVotingState == "Open")
        {
            await services.GetRequiredService<CloseMvpVotingHandler>().HandleAsync(matchId, organizerId, cancellationToken);
        }

        await services.GetRequiredService<GenerateMatchSummaryHandler>().HandleAsync(matchId, organizerId, cancellationToken);
        _logger.LogInformation("Abandoned match {MatchId} left on the scoreboard: ended and summarized.", matchId);
    }
}
