namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Represents a single participant's MVP ballot within a <see cref="MvpVoting"/> session.
/// A participant holds at most one vote per match; replacing a vote updates <see cref="VotedPlayerId"/>.
/// </summary>
public sealed class MvpVote
{
    // Private parameterless constructor required by EF Core.
    private MvpVote() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>mvp_votings.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid VotingId { get; private set; }

    /// <summary>
    /// Denormalized app-layer FK to <c>matches.id</c> for efficient per-match queries.
    /// No DDL FK (module boundary rule).
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid VoterPlayerId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid VotedPlayerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="MvpVote"/> instance.
    /// </summary>
    public static MvpVote Create(
        Guid votingId,
        Guid matchId,
        Guid voterPlayerId,
        Guid votedPlayerId,
        DateTimeOffset now)
    {
        return new MvpVote
        {
            Id = Guid.NewGuid(),
            VotingId = votingId,
            MatchId = matchId,
            VoterPlayerId = voterPlayerId,
            VotedPlayerId = votedPlayerId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the voted-for player while the session is still open. Keeps the single-ballot invariant.
    /// </summary>
    public void ChangeVote(Guid votedPlayerId, DateTimeOffset now)
    {
        VotedPlayerId = votedPlayerId;
        UpdatedAt = now;
    }
}
