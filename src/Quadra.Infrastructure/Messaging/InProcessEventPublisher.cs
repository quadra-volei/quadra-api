using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/> that delivers each event straight to the
/// <see cref="IEventHandler{TEvent}"/> implementations registered in this process — no queue and
/// no separate worker. Handlers run one after another, inside the publishing call, each event in
/// its own DI scope (so a handler never shares a DbContext with the request that published).
///
/// The publisher's own write is already committed when an event is published, so a failing
/// handler is logged and does not fail the caller. There is no retry: a handler that fails
/// leaves its side effect undone until the event is published again.
/// </summary>
public sealed class InProcessEventPublisher : IEventPublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InProcessEventPublisher> _logger;
    private readonly IEventHandlerTracing? _tracing;

    public InProcessEventPublisher(
        IServiceScopeFactory scopeFactory,
        ILogger<InProcessEventPublisher> logger,
        IEventHandlerTracing? tracing = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _tracing = tracing;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            using var trace = _tracing?.Start(typeof(TEvent).Name, handler.GetType().Name);
            try
            {
                // Not the caller's token: these are follow-up effects of a write that has already
                // been committed, and must not be abandoned because the client disconnected.
                await handler.HandleAsync(@event, CancellationToken.None);
            }
            catch (Exception ex)
            {
                trace?.Fail(ex);
                _logger.LogError(
                    ex,
                    "Event handler {Handler} failed for {EventType}. Its side effect was not applied.",
                    handler.GetType().Name,
                    typeof(TEvent).Name);
            }
        }
    }
}
