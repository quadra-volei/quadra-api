using Sentry;

namespace Quadra.Api.Observability;

/// <summary>
/// Wraps one pass of a background job (the sweepers) so it shows up in Sentry like a request:
/// a transaction named after the job, with the job's database queries as child spans, how many
/// items it handled and how late the oldest of them was. There is no queue in this API (events
/// run in-process), so "how late is the oldest due item" is what tells a job is falling behind.
///
/// When <c>Observability:CronMonitors:&lt;job&gt;</c> holds a Sentry monitor slug, each pass also
/// sends a cron check-in, so Sentry alerts when the job stops running. Empty = no check-in (the
/// free Sentry plan includes a single cron monitor). Everything here is a no-op while
/// <c>Sentry:Dsn</c> is empty.
/// </summary>
public sealed class JobMonitor
{
    public const string CronMonitorsSection = "Observability:CronMonitors";
    public const string LagAlertSettingKey = "Observability:JobLagAlertMinutes";
    public const int DefaultLagAlertMinutes = 10;

    private readonly IHub _hub;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JobMonitor> _logger;

    public JobMonitor(IHub hub, IConfiguration configuration, ILogger<JobMonitor> logger)
    {
        _hub = hub;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Runs <paramref name="pass"/> inside a Sentry transaction for <paramref name="job"/>.
    /// Exceptions are recorded and rethrown: each sweeper keeps its own handling.
    /// </summary>
    public async Task RunAsync(string job, TimeSpan interval, Func<JobRun, Task> pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);

        var slug = _configuration[$"{CronMonitorsSection}:{job}"];
        SentryId? checkInId = string.IsNullOrWhiteSpace(slug)
            ? null
            : _hub.CaptureCheckIn(slug, CheckInStatus.InProgress, configureMonitorOptions: options => ConfigureMonitor(options, interval));

        var startedAt = DateTimeOffset.UtcNow;
        var transaction = _hub.StartTransaction(job, "job");
        var run = new JobRun(transaction);

        // A scope of its own, so the database spans of this pass attach to this transaction.
        using var scope = _hub.PushScope();
        _hub.ConfigureScope(s => s.Transaction = transaction);

        try
        {
            await pass(run);
            transaction.Finish(SpanStatus.Ok);
            Report(job, checkInId, slug, CheckInStatus.Ok, startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transaction.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            transaction.Finish(ex);
            Report(job, checkInId, slug, CheckInStatus.Error, startedAt);
            throw;
        }

        WarnIfBehind(job, run);
    }

    private void Report(string job, SentryId? checkInId, string? slug, CheckInStatus status, DateTimeOffset startedAt)
    {
        if (checkInId is { } id && slug is not null)
        {
            _hub.CaptureCheckIn(slug, status, id, DateTimeOffset.UtcNow - startedAt);
        }

        _logger.LogDebug("Job {Job} finished with {Status}.", job, status);
    }

    private void WarnIfBehind(string job, JobRun run)
    {
        var alertAfter = TimeSpan.FromMinutes(_configuration.GetValue(LagAlertSettingKey, DefaultLagAlertMinutes));
        if (alertAfter > TimeSpan.Zero && run.OldestLag is { } lag && lag > alertAfter)
        {
            // Error level so Sentry opens an issue (and emails) when a job falls behind.
            _logger.LogError(
                "Job {Job} is behind: the oldest due item was {LagMinutes:F0} min late ({Items} item(s) in this pass).",
                job,
                lag.TotalMinutes,
                run.Items);
        }
    }

    private static void ConfigureMonitor(SentryMonitorOptions options, TimeSpan interval)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(interval.TotalMinutes));
        options.Interval(minutes, SentryMonitorInterval.Minute);
        options.CheckInMargin = TimeSpan.FromMinutes(Math.Max(2, minutes));
        options.MaxRuntime = TimeSpan.FromMinutes(10);
    }
}

/// <summary>What one pass of a job reports back: items handled and how late the oldest was.</summary>
public sealed class JobRun
{
    private readonly ITransactionTracer _transaction;

    internal JobRun(ITransactionTracer transaction) => _transaction = transaction;

    public int Items { get; private set; }

    public TimeSpan? OldestLag { get; private set; }

    public void SetItems(int items)
    {
        Items = items;
        _transaction.SetMeasurement("job.items", items, MeasurementUnit.None);
    }

    public void SetOldestLag(TimeSpan lag)
    {
        OldestLag = lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
        _transaction.SetMeasurement("job.oldest_lag", OldestLag.Value.TotalSeconds, MeasurementUnit.Duration.Second);
    }
}
