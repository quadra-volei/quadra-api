namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module projection of a match's grouping identity, exposed by the Matches module
/// through <see cref="IMatchGroupReader"/>. Consumed by the Gamification group-ranking read endpoint
/// (to validate the match and resolve the grouping key + display name) and by the Background Worker
/// (to compose the finished-match points DTO). In the MVP <see cref="GroupId"/> equals
/// <see cref="MatchId"/> for a recurring match (SCOPE ruling 2026-07-03).
/// </summary>
public sealed record MatchGroupDescriptor(
    Guid MatchId,
    Guid GroupId,
    string Type,                  // Recurring | OneOff
    string Name,
    DateTimeOffset DateTime);
