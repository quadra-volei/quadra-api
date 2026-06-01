using Microsoft.Extensions.Logging;

namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Default <see cref="IEventPublisher"/> implementation that logs the event payload and discards it.
/// Used until the SQS publisher spec is delivered. Production deployments MUST override this
/// registration with a real publisher.
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
