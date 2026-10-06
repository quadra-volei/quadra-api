namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing a single team with its members.
/// </summary>
public sealed record TeamResponse(
    Guid Id,
    Guid MatchId,
    string Name,
    IReadOnlyList<TeamMemberResponse> Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
