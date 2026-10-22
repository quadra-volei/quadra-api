using Quadra.Modules.Geo.Venues;

namespace Quadra.Api.Background;

/// <summary>
/// Deletes arena coordinates past what Google's terms allow us to keep (30 days, DECISIONS #82),
/// so even arenas nobody looks at again leave the database. Runs every few hours.
/// </summary>
public sealed class VenueCacheSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VenueCacheSweeper> _logger;

    public VenueCacheSweeper(IServiceScopeFactory scopeFactory, ILogger<VenueCacheSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
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
            var purged = await scope.ServiceProvider.GetRequiredService<VenueDiscovery>().PurgeExpiredAsync(cancellationToken);
            if (purged > 0)
            {
                _logger.LogInformation("Purged {Count} arena(s) with expired coordinates.", purged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arena cache sweep failed; it will run again on the next tick.");
        }
    }
}
