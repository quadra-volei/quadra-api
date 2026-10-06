namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing a single player member within a team.
/// </summary>
public sealed record TeamMemberResponse(
    Guid Id,
    Guid TeamId,
    Guid PlayerId,
    string PlayerLevel,
    DateTimeOffset CreatedAt,
    bool IsGuest = false);      // a guest of the match (no account): PlayerId is the guest id
