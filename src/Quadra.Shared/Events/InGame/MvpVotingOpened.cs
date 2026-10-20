namespace Quadra.Shared.Events.InGame;

/// <summary>The organizer opened the MVP vote of a finished game.</summary>
public sealed record MvpVotingOpened(Guid MatchId, DateTimeOffset DeadlineAt, DateTimeOffset OccurredAt);
