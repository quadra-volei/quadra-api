namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Best-of-N format of a volleyball match scoreboard.
/// </summary>
public enum ScoreboardFormat
{
    /// <summary>One game to the match's points (DECISIONS #72); with rotating teams, one game per pair until the organizer ends.</summary>
    SingleGame,
    BestOf3,
    BestOf5,

    /// <summary>
    /// No fixed teams (DECISIONS #86): the match only starts and ends, with no score. Its one
    /// roster holds everyone confirmed; teams form and change on court.
    /// </summary>
    Free,
}
