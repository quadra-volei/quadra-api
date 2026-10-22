namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// The actions one player made in one match, as the organizer recorded them: plain counts, no
/// attempts or errors. One row per <c>(match, player)</c>; a player without a row had nothing
/// recorded, which is not the same as zero. Editable until the match summary is generated.
/// </summary>
public sealed class PlayerMatchStats
{
    /// <summary>Highest count accepted for one action in one match.</summary>
    public const int MaxCount = 99;

    // Private parameterless constructor required by EF Core.
    private PlayerMatchStats() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>.</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>Cortadas (attacks that scored).</summary>
    public int Kills { get; private set; }

    public int Blocks { get; private set; }

    public int Aces { get; private set; }

    /// <summary>Salvadas (digs: an attack kept alive).</summary>
    public int Digs { get; private set; }

    /// <summary>Assistências (the set that led to a point).</summary>
    public int Assists { get; private set; }

    /// <summary>The organizer who last wrote the row.</summary>
    public Guid RecordedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PlayerMatchStats Create(Guid matchId, Guid playerId, Guid recordedBy, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        PlayerId = playerId,
        RecordedBy = recordedBy,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Set(int kills, int blocks, int aces, int digs, int assists, Guid recordedBy, DateTimeOffset now)
    {
        Kills = kills;
        Blocks = blocks;
        Aces = aces;
        Digs = digs;
        Assists = assists;
        RecordedBy = recordedBy;
        UpdatedAt = now;
    }
}
