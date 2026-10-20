using Quadra.Modules.Matches.Application;

namespace Quadra.Api.Background;

/// <summary>
/// Periodically opens and closes confirmation windows that are due, in-process (there is no
/// separate worker). Requests that touch a match sync it themselves; this sweep covers matches
/// nobody is looking at, so they still show up as open on the map and in lists.
///
/// Interval: <c>Matches:WindowSweepSeconds</c> (default 60). Zero or negative disables it.
/// </summary>
public sealed class MatchWindowSweeper : BackgroundService
{
    public const string IntervalSettingKey = "Matches:WindowSweepSeconds";
    public const int DefaultIntervalSeconds = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MatchWindowSweeper> _logger;

    public MatchWindowSweeper(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MatchWindowSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = _configuration.GetValue(IntervalSettingKey, DefaultIntervalSeconds);
        if (seconds <= 0)
        {
            _logger.LogInformation("Match window sweep is disabled.");
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

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<MatchWindowSynchronizer>();
            await synchronizer.SyncDueAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One failed sweep (e.g. the database is briefly unreachable) must not stop the loop.
            _logger.LogError(ex, "Match window sweep failed; it will run again on the next tick.");
        }
    }
}
