namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// The descriptive settings the mobile create-match form collects on top of the core schedule
/// and slots (frontend S11). All optional: a match created without them is an open match with
/// no declared format, level or duration.
/// </summary>
/// <param name="Format">Players per side: <c>2X2</c>, <c>4X4</c> or <c>6X6</c>.</param>
/// <param name="Level">Skill level the match is aimed at.</param>
/// <param name="DurationMinutes">Planned playing time.</param>
/// <param name="Visibility">Open (listed, anyone joins) or Private.</param>
/// <param name="InviteMode">How players join a private match; null for an open one.</param>
/// <param name="PriceMonthly">Monthly price for regulars of a recurring match.</param>
/// <param name="RecurrenceDays">ISO week days (1 = Monday … 7 = Sunday) a recurring match repeats on.</param>
public sealed record MatchSettings(
    string? Format = null,
    MatchLevel? Level = null,
    int? DurationMinutes = null,
    MatchVisibility Visibility = MatchVisibility.Open,
    MatchInviteMode? InviteMode = null,
    decimal? PriceMonthly = null,
    IReadOnlyList<int>? RecurrenceDays = null)
{
    public static readonly IReadOnlyList<string> Formats = ["2X2", "4X4", "6X6"];
}
