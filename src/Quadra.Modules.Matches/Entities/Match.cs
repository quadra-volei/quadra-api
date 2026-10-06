using System.Security.Cryptography;
using NetTopologySuite.Geometries;
using Quadra.Modules.Matches.Application;

namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Aggregate root representing a volleyball match.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class Match
{
    // Private parameterless constructor required by EF Core.
    private Match() { }

    public Guid Id { get; private set; }
    public Guid OrganizerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Address { get; private set; } = null!;

    /// <summary>
    /// PostGIS geography point (SRID 4326). X = Longitude, Y = Latitude.
    /// </summary>
    public Point Location { get; private set; } = null!;

    public DateTimeOffset DateTime { get; private set; }
    public short MaxPlayers { get; private set; }
    public short RegularSlots { get; private set; }

    /// <summary>
    /// Computed column: <c>max_players - regular_slots</c>. Getter-only — never set by application code.
    /// </summary>
    public short DropInSlots { get; private set; }

    public decimal? Price { get; private set; }
    public MatchType Type { get; private set; }
    public MatchFrequency? Frequency { get; private set; }

    /// <summary>
    /// ISO day-of-week (1 = Monday … 7 = Sunday). Only set for <see cref="MatchType.Recurring"/> matches.
    /// Note: C# <see cref="System.DayOfWeek"/> uses 0 = Sunday. Conversion is applied in <see cref="Create"/>.
    /// </summary>
    public short? DayOfWeek { get; private set; }

    public DateTimeOffset WindowOpensAt { get; private set; }
    public DateTimeOffset WindowClosesAt { get; private set; }
    public MatchStatus Status { get; private set; }

    /// <summary>
    /// Number of Regular slots freed up for DropIn use after the confirmation window closes.
    /// Set by <c>CloseMatchWindowHandler</c>. Null until the window has been closed.
    /// </summary>
    public short? ReleasedDropInSlots { get; private set; }

    /// <summary>Players per side: <c>2X2</c>, <c>4X4</c> or <c>6X6</c>. Null when not declared.</summary>
    public string? Format { get; private set; }

    /// <summary>Skill level the match is aimed at. Null when not declared.</summary>
    public MatchLevel? Level { get; private set; }

    /// <summary>Planned playing time in minutes. Null when not declared.</summary>
    public short? DurationMinutes { get; private set; }

    /// <summary>Open matches are listed and joinable by anyone; private ones are not.</summary>
    public MatchVisibility Visibility { get; private set; }

    /// <summary>How players join a private match. Null for an open match.</summary>
    public MatchInviteMode? InviteMode { get; private set; }

    /// <summary>
    /// Code that lets its holder join a private match with <see cref="MatchInviteMode.Code"/>.
    /// Only ever shown to the organizer.
    /// </summary>
    public string? InviteCode { get; private set; }

    /// <summary>Monthly price for regulars of a recurring match.</summary>
    public decimal? PriceMonthly { get; private set; }

    /// <summary>
    /// ISO week days (1 = Monday … 7 = Sunday) a recurring match repeats on. When set,
    /// <see cref="DayOfWeek"/> holds the first of them.
    /// </summary>
    public int[]? RecurrenceDays { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="Match"/> instance.
    /// </summary>
    public static Match Create(
        Guid organizerId,
        string name,
        string? description,
        string address,
        double latitude,
        double longitude,
        DateTimeOffset dateTime,
        int maxPlayers,
        int regularSlots,
        decimal? price,
        MatchType type,
        MatchFrequency? frequency,
        int? dayOfWeekIso,
        DateTimeOffset windowOpensAt,
        DateTimeOffset windowClosesAt,
        DateTimeOffset now,
        MatchSettings? settings = null)
    {
        settings ??= new MatchSettings();
        var recurrenceDays = settings.RecurrenceDays is { Count: > 0 } days
            ? days.Distinct().Order().ToArray()
            : null;

        // PostGIS Point: X = longitude, Y = latitude, SRID 4326.
        var location = new Point(longitude, latitude) { SRID = 4326 };

        return new Match
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Name = name,
            Description = description,
            Address = address,
            Location = location,
            DateTime = dateTime,
            MaxPlayers = (short)maxPlayers,
            RegularSlots = (short)regularSlots,
            // Mirror the DB-computed column so in-memory objects behave consistently.
            DropInSlots = (short)(maxPlayers - regularSlots),
            Price = price,
            Type = type,
            Frequency = frequency,
            DayOfWeek = dayOfWeekIso.HasValue
                ? (short)dayOfWeekIso.Value
                : recurrenceDays is null ? null : (short)recurrenceDays[0],
            WindowOpensAt = windowOpensAt,
            WindowClosesAt = windowClosesAt,
            Status = MatchStatus.Draft,
            Format = settings.Format,
            Level = settings.Level,
            DurationMinutes = settings.DurationMinutes.HasValue ? (short)settings.DurationMinutes.Value : null,
            Visibility = settings.Visibility,
            InviteMode = settings.Visibility == MatchVisibility.Private ? settings.InviteMode : null,
            InviteCode = settings is { Visibility: MatchVisibility.Private, InviteMode: MatchInviteMode.Code }
                ? GenerateInviteCode()
                : null,
            PriceMonthly = settings.PriceMonthly,
            RecurrenceDays = recurrenceDays,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Transitions the match from <see cref="MatchStatus.Draft"/> to <see cref="MatchStatus.Open"/>.
    /// Throws <see cref="InvalidMatchStatusTransitionException"/> if not currently <see cref="MatchStatus.Draft"/>.
    /// </summary>
    /// <summary>
    /// Whether <paramref name="playerId"/> may put themselves on the presence list: the organizer
    /// always can; anyone can for an open match; a private match needs its invite code.
    /// </summary>
    public bool AllowsSelfEnrollment(Guid playerId, string? inviteCode)
    {
        if (playerId == OrganizerId || Visibility == MatchVisibility.Open)
        {
            return true;
        }

        return InviteMode == MatchInviteMode.Code
            && !string.IsNullOrWhiteSpace(inviteCode)
            && string.Equals(inviteCode.Trim(), InviteCode, StringComparison.OrdinalIgnoreCase);
    }

    // 8 characters from an alphabet without look-alikes (no 0/O, 1/I/L).
    private static string GenerateInviteCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return RandomNumberGenerator.GetString(alphabet, 8);
    }

    public void OpenWindow(DateTimeOffset now)
    {
        if (Status is not MatchStatus.Draft)
        {
            throw new InvalidMatchStatusTransitionException(
                $"Cannot open the confirmation window for a match with status '{Status}'. Match must be in Draft.");
        }

        Status = MatchStatus.Open;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the match from <see cref="MatchStatus.Open"/> to <see cref="MatchStatus.Closed"/>.
    /// Stores how many Regular slots were released to DropIn players.
    /// Throws <see cref="InvalidMatchStatusTransitionException"/> if not currently <see cref="MatchStatus.Open"/>.
    /// </summary>
    public void CloseWindow(int releasedSlots, DateTimeOffset now)
    {
        if (Status is not MatchStatus.Open)
        {
            throw new InvalidMatchStatusTransitionException(
                $"Cannot close the confirmation window for a match with status '{Status}'. Match must be Open.");
        }

        Status = MatchStatus.Closed;
        ReleasedDropInSlots = (short)releasedSlots;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the match to <see cref="MatchStatus.Cancelled"/>.
    /// Throws <see cref="InvalidMatchStatusTransitionException"/> if the match is
    /// <see cref="MatchStatus.InProgress"/> or <see cref="MatchStatus.Ended"/> (terminal states).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is MatchStatus.InProgress or MatchStatus.Ended)
        {
            throw new InvalidMatchStatusTransitionException(
                $"Cannot cancel a match with status '{Status}'. Only matches that are not InProgress or Ended can be cancelled.");
        }

        Status = MatchStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the match to <see cref="MatchStatus.Ended"/> when the immutable summary is finalized (F1.6).
    /// Valid from <see cref="MatchStatus.Closed"/> or <see cref="MatchStatus.InProgress"/>.
    /// No-op when already <see cref="MatchStatus.Ended"/>.
    /// Throws <see cref="InvalidMatchStatusTransitionException"/> when the match is <see cref="MatchStatus.Cancelled"/>.
    /// </summary>
    /// <returns><c>true</c> when the status actually changed; <c>false</c> when it was already Ended.</returns>
    public bool MarkEnded(DateTimeOffset now)
    {
        if (Status is MatchStatus.Ended)
        {
            return false;
        }

        if (Status is not (MatchStatus.Closed or MatchStatus.InProgress))
        {
            throw new InvalidMatchStatusTransitionException(
                $"Cannot end a match with status '{Status}'. Only Closed or InProgress matches can be ended.");
        }

        Status = MatchStatus.Ended;
        UpdatedAt = now;
        return true;
    }
}
