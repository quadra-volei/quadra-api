namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Reacts to an integration event of type <typeparamref name="TEvent"/>. Handlers are resolved
/// from DI (scoped) by <see cref="InProcessEventPublisher"/>, one fresh scope per published event.
/// A handler must be idempotent: the same event may reach it more than once.
/// </summary>
public interface IEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
