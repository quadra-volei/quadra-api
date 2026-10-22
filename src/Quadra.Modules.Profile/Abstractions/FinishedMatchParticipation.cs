namespace Quadra.Modules.Profile.Abstractions;

/// <summary>
/// Self-contained description of one player's participation in one finished match, composed by the
/// Background Worker (from cross-module reads) and handed to the Profile module. Profile never
/// reaches into <c>Matches</c>/<c>InGame</c> tables — it only receives this DTO.
/// </summary>
public sealed record FinishedMatchParticipation(
    Guid PlayerId,
    Guid MatchId,
    string MatchName,
    DateTimeOffset MatchDateTime,
    Guid? TeamId,
    string Outcome,
    bool WasMvp,
    int? DurationSeconds,
    DateTimeOffset FinishedAt,
    string? Format = null,      // match format (2X2 | 4X4 | 6X6)
    int? SetsWon = null,        // sets won by the player's team
    int? SetsLost = null,       // sets won by the best opposing team
    PlayerMatchActions? Actions = null,  // the player's sheet; null = none recorded
    int? RalliesPlayed = null,  // points played while the player's team was on court
    int? PlannedMinutes = null); // the match's planned duration (exposure without sets)

/// <summary>The actions recorded for the player in the match; a null action was not recorded.</summary>
public sealed record PlayerMatchActions(int? Kills, int? Blocks, int? Aces, int? Digs, int? Assists);
