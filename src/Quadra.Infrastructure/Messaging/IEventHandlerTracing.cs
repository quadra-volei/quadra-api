namespace Quadra.Infrastructure.Messaging;

/// <summary>
/// Optional hook that <see cref="InProcessEventPublisher"/> calls around each handler, so the
/// host can record it as a span of the request (or job) that published the event. The host
/// implements it with Sentry; with none registered, handlers run untraced.
/// </summary>
public interface IEventHandlerTracing
{
    /// <summary>Starts timing one handler. Dispose the result when the handler returns.</summary>
    IEventHandlerTrace Start(string eventType, string handler);
}

/// <summary>One traced handler run. Disposing it ends the span.</summary>
public interface IEventHandlerTrace : IDisposable
{
    /// <summary>Marks the run as failed before it is disposed.</summary>
    void Fail(Exception exception);
}
