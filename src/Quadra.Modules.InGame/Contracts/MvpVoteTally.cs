namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Per-candidate vote tally, revealed only once voting is Closed.
/// </summary>
public sealed record MvpVoteTally(
    Guid PlayerId,
    int Votes);
