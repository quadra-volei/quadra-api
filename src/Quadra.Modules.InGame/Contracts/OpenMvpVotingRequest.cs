namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Request to open the MVP voting session for an ended match.
/// </summary>
public sealed record OpenMvpVotingRequest(
    DateTimeOffset? DeadlineAt); // optional; null => opened_at + 24h
