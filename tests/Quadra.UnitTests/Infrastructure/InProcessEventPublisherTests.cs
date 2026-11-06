using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quadra.Infrastructure.Messaging;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="InProcessEventPublisher"/>: events are delivered to the handlers
/// registered in the same process, and a failing handler never fails the publisher's caller.
/// </summary>
public sealed class InProcessEventPublisherTests
{
    private sealed record SomethingHappened(int Value);

    private sealed record UnhandledEvent;

    private sealed class Recorder
    {
        public List<string> Calls { get; } = [];
    }

    private sealed class FirstHandler(Recorder recorder) : IEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened @event, CancellationToken cancellationToken)
        {
            recorder.Calls.Add($"first:{@event.Value}:{cancellationToken.CanBeCanceled}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler(Recorder recorder) : IEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened @event, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("failing");
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class LastHandler(Recorder recorder) : IEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened @event, CancellationToken cancellationToken)
        {
            recorder.Calls.Add($"last:{@event.Value}");
            return Task.CompletedTask;
        }
    }

    private static (InProcessEventPublisher Sut, Recorder Recorder) CreateSut()
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IEventHandler<SomethingHappened>, FirstHandler>();
        services.AddScoped<IEventHandler<SomethingHappened>, FailingHandler>();
        services.AddScoped<IEventHandler<SomethingHappened>, LastHandler>();

        var provider = services.BuildServiceProvider(validateScopes: true);
        var sut = new InProcessEventPublisher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InProcessEventPublisher>.Instance);
        return (sut, recorder);
    }

    /// <summary>
    /// Covers: every registered handler receives the event, in registration order, and a handler
    /// that throws neither stops the remaining handlers nor surfaces to the publisher's caller.
    /// </summary>
    [Fact]
    public async Task Delivers_to_every_handler_even_when_one_fails()
    {
        var (sut, recorder) = CreateSut();

        var act = async () => await sut.PublishAsync(new SomethingHappened(7), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        recorder.Calls.Should().Equal("first:7:False", "failing", "last:7");
    }

    /// <summary>
    /// Covers: handlers are not tied to the caller's cancellation — they are follow-up effects of
    /// a write that is already committed.
    /// </summary>
    [Fact]
    public async Task Handlers_run_even_if_the_callers_token_is_already_cancelled()
    {
        var (sut, recorder) = CreateSut();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await sut.PublishAsync(new SomethingHappened(1), cancelled.Token);

        recorder.Calls.Should().HaveCount(3);
    }

    /// <summary>
    /// Covers: an event nobody handles is simply dropped.
    /// </summary>
    [Fact]
    public async Task Event_without_handlers_is_a_no_op()
    {
        var (sut, recorder) = CreateSut();

        await sut.PublishAsync(new UnhandledEvent(), TestContext.Current.CancellationToken);

        recorder.Calls.Should().BeEmpty();
    }

    private sealed class RecordingTracing : IEventHandlerTracing
    {
        public List<string> Calls { get; } = [];

        public IEventHandlerTrace Start(string eventType, string handler)
        {
            Calls.Add($"start:{eventType}:{handler}");
            return new Trace(this, handler);
        }

        private sealed class Trace(RecordingTracing owner, string handler) : IEventHandlerTrace
        {
            public void Fail(Exception exception) => owner.Calls.Add($"fail:{handler}:{exception.Message}");

            public void Dispose() => owner.Calls.Add($"end:{handler}");
        }
    }

    /// <summary>
    /// Covers: with a tracing hook registered, every handler run is wrapped in its own trace,
    /// and a failing handler marks its trace as failed before it ends.
    /// </summary>
    [Fact]
    public async Task Traces_each_handler_and_marks_failures()
    {
        var recorder = new Recorder();
        var tracing = new RecordingTracing();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IEventHandler<SomethingHappened>, FirstHandler>();
        services.AddScoped<IEventHandler<SomethingHappened>, FailingHandler>();
        var provider = services.BuildServiceProvider(validateScopes: true);
        var sut = new InProcessEventPublisher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InProcessEventPublisher>.Instance,
            tracing);

        await sut.PublishAsync(new SomethingHappened(1), TestContext.Current.CancellationToken);

        tracing.Calls.Should().Equal(
            "start:SomethingHappened:FirstHandler",
            "end:FirstHandler",
            "start:SomethingHappened:FailingHandler",
            "fail:FailingHandler:boom",
            "end:FailingHandler");
    }
}
