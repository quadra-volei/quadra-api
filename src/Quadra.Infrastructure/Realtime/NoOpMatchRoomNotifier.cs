using Microsoft.Extensions.Logging;
using Quadra.Shared.Realtime;

namespace Quadra.Infrastructure.Realtime;

/// <summary>
/// Default <see cref="IMatchRoomNotifier"/> implementation that logs the message payload and discards it.
/// Used until the SignalR hub implementation in <c>Quadra.Modules.Realtime</c> is delivered.
/// Production deployments MUST override this registration with the real SignalR notifier.
/// </summary>
public sealed class NoOpMatchRoomNotifier : IMatchRoomNotifier
{
    private readonly ILogger<NoOpMatchRoomNotifier> _logger;

    public NoOpMatchRoomNotifier(ILogger<NoOpMatchRoomNotifier> logger)
    {
        _logger = logger;
    }

    public Task NotifyPresenceUpdatedAsync(PresenceUpdatedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "NoOpMatchRoomNotifier: discarded PresenceUpdated message for MatchId={MatchId} PlayerId={PlayerId}.",
            message.MatchId, message.PlayerId);
        return Task.CompletedTask;
    }

    public Task NotifyScoreboardUpdatedAsync(ScoreboardUpdatedMessage message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "NoOpMatchRoomNotifier: discarded ScoreboardUpdated message for MatchId={MatchId} State={State}.",
            message.MatchId, message.State);
        return Task.CompletedTask;
    }
}
