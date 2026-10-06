namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Represents one of the two teams formed for a match.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class Team
{
    // Private parameterless constructor required by EF Core.
    private Team() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    /// <summary>Team name within the match (e.g. "Team A" / "Team B"). Unique per match.</summary>
    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<TeamMember> _members = [];

    /// <summary>
    /// Members belonging to this team.
    /// Populated in-memory during draft construction and by the repository after loading.
    /// </summary>
    public IReadOnlyList<TeamMember> Members => _members.AsReadOnly();

    /// <summary>
    /// Factory method — the only way to create a new <see cref="Team"/> instance.
    /// </summary>
    public static Team Create(Guid matchId, string name, DateTimeOffset now)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Adds a new <see cref="TeamMember"/> to this team.
    /// </summary>
    public TeamMember AddMember(Guid playerId, DateTimeOffset now)
    {
        var member = TeamMember.Create(Id, MatchId, playerId, now);
        _members.Add(member);
        return member;
    }

    /// <summary>
    /// Appends members loaded externally (used by the repository after a query).
    /// </summary>
    internal void AttachMembers(IEnumerable<TeamMember> members)
    {
        _members.AddRange(members);
    }
}
