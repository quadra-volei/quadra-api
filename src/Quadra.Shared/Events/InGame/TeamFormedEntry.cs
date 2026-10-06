namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Describes one team within a <see cref="TeamsFormed"/> integration event.
/// </summary>
public sealed record TeamFormedEntry(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<Guid> PlayerIds);
