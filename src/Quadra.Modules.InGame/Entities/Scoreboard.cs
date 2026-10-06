namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Aggregate root representing the live set-by-set scoreboard for a match.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class Scoreboard
{
    // Private parameterless constructor required by EF Core.
    private Scoreboard() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    public ScoreboardFormat Format { get; private set; }

    public ScoreboardState State { get; private set; }

    /// <summary>App-layer FK to <c>teams.id</c> (owned by InGame, F1.3).</summary>
    public Guid TeamAId { get; private set; }

    /// <summary>App-layer FK to <c>teams.id</c> (owned by InGame, F1.3).</summary>
    public Guid TeamBId { get; private set; }

    public int TeamASetsWon { get; private set; }
    public int TeamBSetsWon { get; private set; }

    /// <summary><c>0</c> before start; <c>1..5</c> while playing.</summary>
    public int CurrentSetNumber { get; private set; }

    /// <summary>Set only when <see cref="State"/> is <see cref="ScoreboardState.Ended"/>; <c>null</c> on a forced end with equal sets.</summary>
    public Guid? WinnerTeamId { get; private set; }

    /// <summary>
    /// True when the match has more than two teams: each set is played by a pair the organizer
    /// picks, so <see cref="TeamAId"/>/<see cref="TeamBId"/> are the pair of the current set.
    /// </summary>
    public bool RotatesTeams { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<ScoreboardSet> _sets = [];

    /// <summary>
    /// Sets belonging to this scoreboard.
    /// Populated by the repository after loading and by <see cref="OpenSet"/> during play.
    /// </summary>
    public IReadOnlyList<ScoreboardSet> Sets => _sets.AsReadOnly();

    /// <summary>
    /// Factory method — the only way to create a new <see cref="Scoreboard"/> instance.
    /// The scoreboard starts in <see cref="ScoreboardState.NotStarted"/> with no sets.
    /// </summary>
    public static Scoreboard Create(
        Guid matchId,
        ScoreboardFormat format,
        Guid teamAId,
        Guid teamBId,
        DateTimeOffset now,
        bool rotatesTeams = false)
    {
        return new Scoreboard
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Format = format,
            State = ScoreboardState.NotStarted,
            TeamAId = teamAId,
            TeamBId = teamBId,
            TeamASetsWon = 0,
            TeamBSetsWon = 0,
            CurrentSetNumber = 0,
            WinnerTeamId = null,
            RotatesTeams = rotatesTeams,
            StartedAt = null,
            EndedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Transitions the scoreboard to <see cref="ScoreboardState.InProgress"/> and moves to set 1.
    /// </summary>
    public void Start(DateTimeOffset now)
    {
        State = ScoreboardState.InProgress;
        StartedAt = now;
        CurrentSetNumber = 1;
        UpdatedAt = now;
    }

    /// <summary>
    /// Creates and attaches a new <see cref="ScoreboardSet"/>, making it the current set.
    /// </summary>
    public ScoreboardSet OpenSet(int setNumber, bool isDecidingSet, DateTimeOffset now)
    {
        var set = ScoreboardSet.Create(Id, MatchId, setNumber, isDecidingSet, now, TeamAId, TeamBId);
        _sets.Add(set);
        CurrentSetNumber = setNumber;
        UpdatedAt = now;
        return set;
    }

    /// <summary>
    /// Increments the set tally for the winning team.
    /// </summary>
    /// <summary>Sets won by a team across the whole game, whichever pairs it played in.</summary>
    public int SetsWonBy(Guid teamId) => _sets.Count(s => s.WinnerTeamId == teamId);

    /// <summary>The team with strictly the most sets won; null on a tie or with no set won.</summary>
    public Guid? LeaderBySets()
    {
        var ranking = _sets
            .Where(s => s.WinnerTeamId is not null)
            .GroupBy(s => s.WinnerTeamId!.Value)
            .Select(g => (TeamId: g.Key, Sets: g.Count()))
            .OrderByDescending(t => t.Sets)
            .ToList();
        return ranking.Count > 0 && (ranking.Count == 1 || ranking[0].Sets > ranking[1].Sets)
            ? ranking[0].TeamId
            : null;
    }

    /// <summary>Puts another pair of teams on court for the next set.</summary>
    public void SetPair(Guid teamAId, Guid teamBId)
    {
        TeamAId = teamAId;
        TeamBId = teamBId;
        TeamASetsWon = SetsWonBy(teamAId);
        TeamBSetsWon = SetsWonBy(teamBId);
    }

    public void RegisterSetWon(Guid winnerTeamId)
    {
        if (winnerTeamId == TeamAId)
        {
            TeamASetsWon++;
        }
        else if (winnerTeamId == TeamBId)
        {
            TeamBSetsWon++;
        }
    }

    /// <summary>
    /// Transitions the scoreboard to <see cref="ScoreboardState.Ended"/>.
    /// <paramref name="winnerTeamId"/> is <c>null</c> on a forced end with equal sets won.
    /// </summary>
    public void End(Guid? winnerTeamId, DateTimeOffset now)
    {
        State = ScoreboardState.Ended;
        WinnerTeamId = winnerTeamId;
        EndedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Appends sets loaded externally (used by the repository after a query).
    /// </summary>
    internal void AttachSets(IEnumerable<ScoreboardSet> sets)
    {
        _sets.AddRange(sets);
    }
}
