namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Everything the match screen needs in one call: the match, who organizes it, the roster with
/// player identities, organizer-added guests, the waiting list and the caller's own standing.
/// </summary>
/// <param name="InviteCode">The private match's invite code — only present for the organizer.</param>
/// <param name="ConfirmedCount">Confirmed players plus guests.</param>
/// <param name="OpenSlots">Slots still free: <c>MaxPlayers - ConfirmedCount</c>, never negative.</param>
/// <param name="MyPresence">The caller's presence, or null when they are not on the list.</param>
/// <param name="MyWaitingListPosition">The caller's waiting-list position, or null.</param>
/// <param name="CanJoin">Whether the caller may put themselves on the list (open match, or organizer). A private match by code reports false: the code is checked when joining.</param>
public sealed record MatchDetailResponse(
    MatchResponse Match,
    string? InviteCode,
    MatchPlayerResponse Organizer,
    IReadOnlyList<MatchRosterEntryResponse> Players,
    IReadOnlyList<MatchGuestResponse> Guests,
    IReadOnlyList<MatchWaitingEntryResponse> WaitingList,
    int ConfirmedCount,
    int OpenSlots,
    MyPresenceResponse? MyPresence,
    int? MyWaitingListPosition,
    bool CanJoin);

/// <summary>A player's public identity inside a match payload.</summary>
public sealed record MatchPlayerResponse(
    Guid UserId,
    string DisplayName,
    string? Handle,
    string? Position,
    string Level,
    string? PhotoUrl);

/// <summary>One presence row with the player's identity.</summary>
public sealed record MatchRosterEntryResponse(
    MatchPlayerResponse Player,
    string PlayerType,
    string Status,
    DateTimeOffset? ConfirmedAt);

/// <summary>A player without an account, added by the organizer. Always counts as confirmed.</summary>
public sealed record MatchGuestResponse(
    Guid Id,
    string Name,
    string? Position,
    DateTimeOffset CreatedAt);

public sealed record MatchWaitingEntryResponse(
    MatchPlayerResponse Player,
    string PlayerType,
    int Position);

public sealed record MyPresenceResponse(string PlayerType, string Status);
