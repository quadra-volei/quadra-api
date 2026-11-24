using Microsoft.Extensions.Options;
using Quadra.Api.Observability;
using Quadra.Modules.Gamification.Billing;

namespace Quadra.Api.Background;

/// <summary>
/// Asks Google Play for the season passes refunded or charged back (DECISIONS #109) and closes the
/// paid track of each. Every <c>SeasonPass:Billing:VoidedSweepMinutes</c> (60; 0 = off); does
/// nothing while billing is not configured.
/// </summary>
public sealed class VoidedPurchaseSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobMonitor _jobMonitor;
    private readonly PlayBillingOptions _options;
    private readonly ILogger<VoidedPurchaseSweeper> _logger;

    public VoidedPurchaseSweeper(
        IServiceScopeFactory scopeFactory,
        JobMonitor jobMonitor,
        IOptions<PlayBillingOptions> options,
        ILogger<VoidedPurchaseSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _jobMonitor = jobMonitor;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.VoidedSweepMinutes <= 0 || !_options.IsConfigured)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.VoidedSweepMinutes);
        // One sweep as soon as the API starts: the free plan restarts and sleeps the instance often,
        // and a timer-only sweep could go more than the 30 days Google keeps voided purchases
        // without running (audit 2026-11-24, voided-purchase-sweep-window-revocation-gap).
        using var timer = new PeriodicTimer(interval);
        try
        {
            await SweepOnceAsync(interval, stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOnceAsync(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    private async Task SweepOnceAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await _jobMonitor.RunAsync("voided-purchase-sweep", interval, async run =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                run.SetItems(await scope.ServiceProvider.GetRequiredService<SeasonPassPurchases>().SweepVoidedAsync(cancellationToken));
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voided purchase sweep failed; it will run again on the next tick.");
        }
    }
}
