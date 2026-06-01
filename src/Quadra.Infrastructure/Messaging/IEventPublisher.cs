namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Cross-module abstraction for publishing integration events to the messaging backbone (SQS).
/// Modules depend on this interface only; the concrete SQS implementation is registered by the
/// infrastructure layer at composition time.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes <paramref name="event"/> to its destination queue / topic. Implementations
    /// must be at-least-once; consumers are expected to be idempotent.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class;
}
