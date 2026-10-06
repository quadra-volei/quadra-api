using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.Realtime;

/// <summary>
/// SignalR hub for live match updates (<c>/hubs/match</c>). A client joins the room of the
/// match it is looking at and then receives <c>ScoreboardUpdated</c> and
/// <c>PresenceUpdated</c> messages for it. Server → client only: every change goes through
/// the REST endpoints.
///
/// The access token travels as the <c>access_token</c> query parameter (WebSockets cannot
/// send headers); the Auth module's JWT events read it for paths under <c>/hubs</c>.
/// </summary>
[Authorize]
public sealed class MatchHub : Hub
{
    public const string Path = "/hubs/match";
    public const string ScoreboardUpdated = "ScoreboardUpdated";
    public const string PresenceUpdated = "PresenceUpdated";

    public static string RoomOf(Guid matchId) => $"match:{matchId:N}";

    public Task JoinMatchRoom(Guid matchId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RoomOf(matchId), Context.ConnectionAborted);

    public Task LeaveMatchRoom(Guid matchId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomOf(matchId), Context.ConnectionAborted);
}

/// <summary><see cref="IMatchRoomNotifier"/> that pushes to the clients in the match room.</summary>
public sealed class SignalRMatchRoomNotifier : IMatchRoomNotifier
{
    private readonly IHubContext<MatchHub> _hub;

    public SignalRMatchRoomNotifier(IHubContext<MatchHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyPresenceUpdatedAsync(PresenceUpdatedMessage message, CancellationToken cancellationToken) =>
        _hub.Clients.Group(MatchHub.RoomOf(message.MatchId))
            .SendAsync(MatchHub.PresenceUpdated, message, cancellationToken);

    public Task NotifyScoreboardUpdatedAsync(ScoreboardUpdatedMessage message, CancellationToken cancellationToken) =>
        _hub.Clients.Group(MatchHub.RoomOf(message.MatchId))
            .SendAsync(MatchHub.ScoreboardUpdated, message, cancellationToken);
}

public static class RealtimeModuleExtensions
{
    /// <summary>
    /// Registers SignalR and the real match-room notifier.
    ///
    /// ponytail: in-memory groups, so it only works with ONE API instance (the case on the
    /// free hosting). Add the Redis backplane (<c>AddStackExchangeRedis</c>) before scaling out.
    /// </summary>
    public static IServiceCollection AddRealtimeModule(this IServiceCollection services)
    {
        services.AddSignalR();
        services.RemoveAll<IMatchRoomNotifier>();
        services.AddSingleton<IMatchRoomNotifier, SignalRMatchRoomNotifier>();
        return services;
    }

    public static IEndpointRouteBuilder MapRealtimeHubs(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<MatchHub>(MatchHub.Path);
        return endpoints;
    }
}
