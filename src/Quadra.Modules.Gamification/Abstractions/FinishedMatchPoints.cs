namespace Quadra.Modules.Gamification.Abstractions;

/// <summary>
/// Self-contained description of a finished recurring match's point-awarding inputs, composed by the
/// Background Worker (from cross-module reads) and handed to the Gamification module. Gamification
/// never reaches into <c>Matches</c>/<c>InGame</c> tables — it only receives this DTO.
/// </summary>
public sealed record FinishedMatchPoints(
    Guid MatchId,
    Guid GroupId,                 // grouping key; == MatchId in the MVP (SCOPE ruling)
    DateTimeOffset MatchDateTime,
    Guid? MvpPlayerId,
    IReadOnlyList<FinishedMatchPlayerPoints> Players,   // all participants who played
    DateTimeOffset FinishedAt);

/// <summary>One participant's outcome within a finished match.</summary>
public sealed record FinishedMatchPlayerPoints(
    Guid PlayerId,
    bool Won);                    // true when the player's team is the winning team
