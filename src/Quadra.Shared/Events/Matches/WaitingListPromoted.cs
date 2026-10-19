namespace Quadra.Shared.Events.Matches;

/// <summary>A spot opened and the first of the waiting list got it (as a pending presence to confirm).</summary>
public sealed record WaitingListPromoted(Guid MatchId, Guid PlayerId, DateTimeOffset OccurredAt);
