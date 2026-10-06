namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// A player without an app account whom the organizer added to fill a slot (frontend S12).
/// A guest always counts as a confirmed player against the match capacity. Guests have no
/// profile, stats or ranking: they only exist inside the match.
/// </summary>
public sealed class MatchGuest
{
    // Private parameterless constructor required by EF Core.
    private MatchGuest() { }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Court position code (LEV, PON, OPO, CEN, LIB, COR), when the organizer set one.</summary>
    public string? Position { get; private set; }

    public Guid AddedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MatchGuest Create(
        Guid matchId,
        string name,
        string? position,
        Guid addedBy,
        DateTimeOffset now)
    {
        return new MatchGuest
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Name = name,
            Position = position,
            AddedBy = addedBy,
            CreatedAt = now,
        };
    }
}
