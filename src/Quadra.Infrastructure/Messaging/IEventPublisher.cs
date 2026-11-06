namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Cross-module abstraction for publishing integration events. Modules depend on this interface
/// only; the host registers <see cref="InProcessEventPublisher"/>, which runs the registered
/// <c>IEventHandler&lt;T&gt;</c> instances in the same process (there is no queue).
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes <paramref name="event"/> to its handlers. Handlers are expected to be idempotent.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class;
}
