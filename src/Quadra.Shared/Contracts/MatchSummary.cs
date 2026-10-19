namespace Quadra.Shared.Contracts;

/// <summary>
/// Minimal projection of a match returned by <see cref="IMatchReader"/>.
/// Only the fields needed by cross-module consumers are exposed here.
/// </summary>
/// <param name="GamePoints">Points that win the single game; null on matches created before it existed.</param>
public sealed record MatchSummary(Guid Id, Guid OrganizerId, string Status, DateTimeOffset StartsAt, int? GamePoints = null);
