namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Team-composition entry within a <see cref="MatchSummaryResponse"/>.
/// </summary>
public record MatchSummaryTeamResponse(
    Guid TeamId,
    string Name,
    IReadOnlyList<Guid> PlayerIds);                  // ordered by PlayerId ascending
