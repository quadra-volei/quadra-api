using Microsoft.Extensions.Logging;

namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Default <see cref="IEventPublisher"/> implementation that logs the event payload and discards it.
/// A module falls back to it only when no publisher is registered (some unit-test hosts); the API
/// registers <see cref="InProcessEventPublisher"/> before the modules.
/// </summary>
public sealed class NoOpEventPublisher : IEventPublisher
{
    private readonly ILogger<NoOpEventPublisher> _logger;

    public NoOpEventPublisher(ILogger<NoOpEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class
    {
        _logger.LogInformation(
            "NoOpEventPublisher: discarded event of type {EventType}.",
            typeof(TEvent).Name);
        return Task.CompletedTask;
    }
}
