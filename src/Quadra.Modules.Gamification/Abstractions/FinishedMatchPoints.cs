namespace Quadra.Modules.Gamification.Abstractions;

/// <summary>
/// Self-contained description of a finished recurring match's point-awarding inputs, composed by the
/// Background Worker (from cross-module reads) and handed to the Gamification module. Gamification
/// never reaches into <c>Matches</c>/<c>InGame</c> tables — it only receives this DTO.
/// </summary>
public sealed record FinishedMatchPoints(
    Guid MatchId,
    Guid GroupId,                 // the recurring group (id of its first game)
    DateTimeOffset MatchDateTime,
    Guid? MvpPlayerId,
    IReadOnlyList<FinishedMatchPlayerPoints> Players,   // all participants who played
    DateTimeOffset FinishedAt,
    int MvpTotalVotes = 0);       // ballots cast; the MVP bonus needs PointRules.MvpMinVotes

/// <summary>One participant's outcome within a finished match.</summary>
public sealed record FinishedMatchPlayerPoints(
    Guid PlayerId,
    int GamesWon = 0,             // games (sets) the player's team won
    int MvpVotes = 0);            // MVP votes the player received
