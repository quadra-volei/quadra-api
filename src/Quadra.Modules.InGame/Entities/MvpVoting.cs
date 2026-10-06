namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Aggregate root representing the post-match MVP voting session for a match.
/// Holds the Organizer-set deadline, the <see cref="MvpVotingState"/> and the decided MVP.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class MvpVoting
{
    // Private parameterless constructor required by EF Core.
    private MvpVoting() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    public MvpVotingState State { get; private set; }

    /// <summary>Organizer-set deadline; defaults to <c>opened_at + 24h</c> when omitted.</summary>
    public DateTimeOffset DeadlineAt { get; private set; }

    /// <summary>Cached count of cast votes (one per voter).</summary>
    public int TotalVotes { get; private set; }

    /// <summary>
    /// Decided MVP; set only when <see cref="State"/> is <see cref="MvpVotingState.Closed"/> and at
    /// least one vote was cast. App-layer FK to <c>users.id</c>; no DDL FK (module boundary rule).
    /// </summary>
    public Guid? MvpPlayerId { get; private set; }

    /// <summary>Winning candidate's vote count; set only when closed with a decided MVP.</summary>
    public int? MvpVoteCount { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>Set when <see cref="State"/> transitions to <see cref="MvpVotingState.Closed"/>.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<MvpVote> _votes = [];

    /// <summary>
    /// Votes belonging to this voting session.
    /// Populated by the repository after loading.
    /// </summary>
    public IReadOnlyList<MvpVote> Votes => _votes.AsReadOnly();

    /// <summary>
    /// Factory method — the only way to open a new <see cref="MvpVoting"/> session.
    /// The session starts in <see cref="MvpVotingState.Open"/> with no votes.
    /// </summary>
    public static MvpVoting Open(Guid matchId, DateTimeOffset deadlineAt, DateTimeOffset now)
    {
        return new MvpVoting
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            State = MvpVotingState.Open,
            DeadlineAt = deadlineAt,
            TotalVotes = 0,
            MvpPlayerId = null,
            MvpVoteCount = null,
            OpenedAt = now,
            ClosedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Increments the cached vote count for a newly cast (not replaced) ballot.
    /// </summary>
    public void RegisterVoteCast()
    {
        TotalVotes++;
    }

    /// <summary>
    /// Transitions the session to <see cref="MvpVotingState.Closed"/>.
    /// <paramref name="mvpPlayerId"/> and <paramref name="mvpVoteCount"/> are <c>null</c> when no vote was cast.
    /// </summary>
    public void Close(Guid? mvpPlayerId, int? mvpVoteCount, DateTimeOffset now)
    {
        State = MvpVotingState.Closed;
        MvpPlayerId = mvpPlayerId;
        MvpVoteCount = mvpVoteCount;
        ClosedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Appends votes loaded externally (used by the repository after a query).
    /// </summary>
    internal void AttachVotes(IEnumerable<MvpVote> votes)
    {
        _votes.AddRange(votes);
    }
}
