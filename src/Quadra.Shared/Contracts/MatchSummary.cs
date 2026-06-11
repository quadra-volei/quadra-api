namespace Quadra.Shared.Contracts;

/// <summary>
/// Minimal projection of a match returned by <see cref="IMatchReader"/>.
/// Only the fields needed by cross-module consumers are exposed here.
/// </summary>
public sealed record MatchSummary(Guid Id, Guid OrganizerId, string Status);
