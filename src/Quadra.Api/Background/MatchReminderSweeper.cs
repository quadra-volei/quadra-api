using Quadra.Shared.Contracts;

namespace Quadra.Api.Background;

/// <summary>
/// "Amanhã tem jogo" (DECISIONS #77): every few minutes, the players of a match that starts within
/// 24 hours get one reminder: confirm if still pending, see you there if confirmed. The dedup key
/// keeps it to one per player and match. <c>Notifications:ReminderSweepSeconds</c> = 0 turns it off.
/// </summary>
public sealed class MatchReminderSweeper : BackgroundService
{
    public const string IntervalSettingKey = "Notifications:ReminderSweepSeconds";
    public const int DefaultIntervalSeconds = 600;

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MatchReminderSweeper> _logger;

    public MatchReminderSweeper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<MatchReminderSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = _configuration.GetValue(IntervalSettingKey, DefaultIntervalSeconds);
        if (seconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
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

    /// <summary>One pass; public so a test can run it without waiting for the timer.</summary>
    public async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var matches = scope.ServiceProvider.GetRequiredService<IUpcomingMatchReader>();
            var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
            var now = _timeProvider.GetUtcNow();

            foreach (var match in await matches.ListStartingBetweenAsync(now, now.AddHours(24), cancellationToken))
            {
                var at = TimeZoneInfo.ConvertTime(match.StartsAt, Brasilia).ToString("HH'h'mm");
                var pending = match.Players.Where(p => !p.Confirmed).Select(p => p.PlayerId).ToList();
                var confirmed = match.Players.Where(p => p.Confirmed).Select(p => p.PlayerId).ToList();
                await sender.SendAsync(
                    pending,
                    new NotificationRequest("ConfirmPresence", NotificationCategory.Reminders, "Vai jogar?",
                        $"{match.Name} é às {at}. Confirme sua presença.", match.MatchId, $"tomorrow:{match.MatchId}"),
                    cancellationToken);
                await sender.SendAsync(
                    confirmed,
                    new NotificationRequest("MatchTomorrow", NotificationCategory.Reminders, "Tem jogo chegando",
                        $"{match.Name} é às {at}. Te vejo na quadra!", match.MatchId, $"tomorrow:{match.MatchId}"),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Match reminder sweep failed; it will run again on the next tick.");
        }
    }
}
