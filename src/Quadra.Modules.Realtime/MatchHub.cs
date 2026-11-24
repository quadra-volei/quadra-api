using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Shared.Contracts;
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

    private readonly IMatchAccessReader _access;

    public MatchHub(IMatchAccessReader access)
    {
        _access = access;
    }

    /// <summary>Joins the room of a match the caller may see (CanViewMatch); refused otherwise.</summary>
    public async Task JoinMatchRoom(Guid matchId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var callerId)
            || !await _access.CanViewAsync(matchId, callerId, Context.ConnectionAborted))
        {
            throw new HubException("Match not found.");
        }

        MatchRooms.Join(matchId, Context.ConnectionId, callerId);
    }

    public Task LeaveMatchRoom(Guid matchId)
    {
        MatchRooms.Leave(matchId, Context.ConnectionId);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        MatchRooms.Drop(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Who is listening to each match room. Access is checked again at every push, not only when a
/// client joins: a match that became private, or a player removed from it, stops reaching the
/// people who can no longer see it (audit 2026-11-24, match-room-stale-membership).
///
/// ponytail: in memory and one CanView query per listener per push, fine for one instance and
/// rooms of a few dozen phones; move to the Redis backplane plus a batched check when scaling out.
/// </summary>
internal static class MatchRooms
{
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, Guid>> Rooms = new();

    public static void Join(Guid matchId, string connectionId, Guid userId) =>
        Rooms.GetOrAdd(matchId, _ => new ConcurrentDictionary<string, Guid>())[connectionId] = userId;

    public static void Leave(Guid matchId, string connectionId)
    {
        if (Rooms.TryGetValue(matchId, out var room))
        {
            room.TryRemove(connectionId, out _);
        }
    }

    public static void Drop(string connectionId)
    {
        foreach (var room in Rooms.Values)
        {
            room.TryRemove(connectionId, out _);
        }
    }

    public static IReadOnlyList<KeyValuePair<string, Guid>> Listeners(Guid matchId) =>
        Rooms.TryGetValue(matchId, out var room) ? room.ToArray() : [];
}

/// <summary><see cref="IMatchRoomNotifier"/> that pushes to the clients in the match room.</summary>
public sealed class SignalRMatchRoomNotifier : IMatchRoomNotifier
{
    private readonly IHubContext<MatchHub> _hub;
    private readonly IServiceScopeFactory _scopes;

    public SignalRMatchRoomNotifier(IHubContext<MatchHub> hub, IServiceScopeFactory scopes)
    {
        _hub = hub;
        _scopes = scopes;
    }

    public Task NotifyPresenceUpdatedAsync(PresenceUpdatedMessage message, CancellationToken cancellationToken) =>
        SendAsync(message.MatchId, MatchHub.PresenceUpdated, message, cancellationToken);

    public Task NotifyScoreboardUpdatedAsync(ScoreboardUpdatedMessage message, CancellationToken cancellationToken) =>
        SendAsync(message.MatchId, MatchHub.ScoreboardUpdated, message, cancellationToken);

    /// <summary>Pushes to each listener of the room that may still see the match; drops the others.</summary>
    private async Task SendAsync(Guid matchId, string method, object message, CancellationToken cancellationToken)
    {
        var listeners = MatchRooms.Listeners(matchId);
        if (listeners.Count == 0)
        {
            return;
        }

        using var scope = _scopes.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IMatchAccessReader>();
        var allowed = new List<string>(listeners.Count);
        foreach (var (connectionId, userId) in listeners)
        {
            if (await access.CanViewAsync(matchId, userId, cancellationToken))
            {
                allowed.Add(connectionId);
            }
            else
            {
                MatchRooms.Leave(matchId, connectionId);
            }
        }

        if (allowed.Count > 0)
        {
            await _hub.Clients.Clients(allowed).SendAsync(method, message, cancellationToken);
        }
    }
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
        // A connection ends when its access token expires (15 min), so a logout or a deleted
        // account stops receiving pushes at the latest then.
        endpoints.MapHub<MatchHub>(MatchHub.Path, o => o.CloseOnAuthenticationExpiration = true);
        return endpoints;
    }
}
