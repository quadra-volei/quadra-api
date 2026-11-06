using Quadra.Infrastructure.Messaging;
using Sentry;

namespace Quadra.Api.Observability;

/// <summary>
/// Records each in-process event handler as a child span of whatever published the event (a
/// request or a background job), so one trace shows request → event → handler → queries.
/// Does nothing when there is no active span (Sentry off, or the trace was not sampled).
/// </summary>
public sealed class SentryEventHandlerTracing : IEventHandlerTracing
{
    private readonly IHub _hub;

    public SentryEventHandlerTracing(IHub hub) => _hub = hub;

    public IEventHandlerTrace Start(string eventType, string handler)
    {
        var parent = _hub.GetSpan();
        if (parent is null)
        {
            return NoTrace.Instance;
        }

        var span = parent.StartChild("event.handler", $"{eventType} → {handler}");
        span.SetTag("event.type", eventType);
        span.SetTag("event.handler", handler);
        return new SpanTrace(_hub, span);
    }

    private sealed class SpanTrace : IEventHandlerTrace
    {
        private readonly IHub _hub;
        private readonly ISpan _span;
        private readonly ISpan? _previous;
        private Exception? _failure;

        public SpanTrace(IHub hub, ISpan span)
        {
            _hub = hub;
            _span = span;
            // While the handler runs, its queries nest under its span instead of the request's.
            _previous = hub.GetSpan();
            hub.ConfigureScope(s => s.Span = span);
        }

        public void Fail(Exception exception) => _failure = exception;

        public void Dispose()
        {
            if (_failure is null)
            {
                _span.Finish(SpanStatus.Ok);
            }
            else
            {
                _span.Finish(_failure);
            }

            _hub.ConfigureScope(s => s.Span = _previous);
        }
    }

    private sealed class NoTrace : IEventHandlerTrace
    {
        public static readonly NoTrace Instance = new();

        public void Fail(Exception exception)
        {
        }

        public void Dispose()
        {
        }
    }
}
