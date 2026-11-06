using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quadra.Api.Observability;
using Sentry.Extensibility;

namespace Quadra.UnitTests.Api;

/// <summary>
/// Unit tests for <see cref="JobMonitor"/>: a job pass runs and reports back even with Sentry
/// off, failures reach the sweeper unchanged, and a job falling behind is logged as an error.
/// </summary>
public sealed class JobMonitorTests
{
    private sealed class RecordingLogger : ILogger<JobMonitor>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (JobMonitor Sut, RecordingLogger Logger) CreateSut(int lagAlertMinutes = 10)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [JobMonitor.LagAlertSettingKey] = lagAlertMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                // A slug with Sentry off must still be harmless.
                [$"{JobMonitor.CronMonitorsSection}:test-job"] = "test-job",
            })
            .Build();
        var logger = new RecordingLogger();
        return (new JobMonitor(DisabledHub.Instance, configuration, logger), logger);
    }

    /// <summary>Covers: the pass runs and what it reports is kept, with Sentry disabled.</summary>
    [Fact]
    public async Task Runs_the_pass_and_keeps_what_it_reports()
    {
        var (sut, logger) = CreateSut();
        JobRun? seen = null;

        await sut.RunAsync("test-job", TimeSpan.FromMinutes(1), run =>
        {
            run.SetItems(3);
            run.SetOldestLag(TimeSpan.FromMinutes(1));
            seen = run;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        seen!.Items.Should().Be(3);
        seen.OldestLag.Should().Be(TimeSpan.FromMinutes(1));
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    /// <summary>Covers: an exception in the pass reaches the caller (each sweeper logs it).</summary>
    [Fact]
    public async Task Rethrows_a_failing_pass()
    {
        var (sut, _) = CreateSut();

        var act = () => sut.RunAsync(
            "test-job",
            TimeSpan.FromMinutes(1),
            _ => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    /// <summary>Covers: a due item later than the alert threshold is logged as an error.</summary>
    [Fact]
    public async Task Logs_an_error_when_the_job_falls_behind()
    {
        var (sut, logger) = CreateSut(lagAlertMinutes: 10);

        await sut.RunAsync("test-job", TimeSpan.FromMinutes(1), run =>
        {
            run.SetItems(2);
            run.SetOldestLag(TimeSpan.FromMinutes(25));
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains("test-job"));
    }
}
