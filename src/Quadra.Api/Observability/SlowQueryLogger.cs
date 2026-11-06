using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Quadra.Api.Observability;

/// <summary>
/// Logs, as a warning, every EF Core command slower than <c>Observability:SlowQueryMilliseconds</c>
/// (default 500; zero or negative turns it off), with its SQL and the module's DbContext. It
/// listens to EF Core's diagnostic events, so every module is covered without touching their
/// DbContext setup. The SQL carries parameter placeholders, not values (sensitive data logging
/// stays off), so no personal data reaches the logs. Sentry records the same queries as spans;
/// this log is what shows them in Render's logs, where no sampling applies.
/// </summary>
public sealed class SlowQueryLogger : IHostedService, IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    public const string ThresholdSettingKey = "Observability:SlowQueryMilliseconds";
    public const int DefaultThresholdMilliseconds = 500;
    private const int MaxSqlLength = 2000;

    private readonly ILogger<SlowQueryLogger> _logger;
    private readonly TimeSpan _threshold;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _gate = new();

    public SlowQueryLogger(IConfiguration configuration, ILogger<SlowQueryLogger> logger)
    {
        _logger = logger;
        _threshold = TimeSpan.FromMilliseconds(configuration.GetValue(ThresholdSettingKey, DefaultThresholdMilliseconds));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_threshold > TimeSpan.Zero)
        {
            lock (_gate)
            {
                _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        return Task.CompletedTask;
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name != DbLoggerCategory.Name)
        {
            return;
        }

        lock (_gate)
        {
            _subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> diagnostic)
    {
        if (diagnostic.Value is not CommandExecutedEventData executed || executed.Duration < _threshold)
        {
            return;
        }

        var sql = executed.Command.CommandText;
        if (sql.Length > MaxSqlLength)
        {
            sql = string.Concat(sql.AsSpan(0, MaxSqlLength), "…");
        }

        _logger.LogWarning(
            "Slow query: {ElapsedMs:F0} ms in {DbContext}. {Sql}",
            executed.Duration.TotalMilliseconds,
            executed.Context?.GetType().Name,
            sql);
    }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    {
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    {
    }
}
