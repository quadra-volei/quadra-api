namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// One action summed over the matches where it was recorded: the count (volume), in how many
/// matches, over how many standard games (<see cref="Application.PlayerSkillCalculator.ExposureGames"/>),
/// and the weight of that evidence (the same games, each match capped at
/// <see cref="Application.PlayerSkillCalculator.MaxGamesPerMatch"/>).
/// </summary>
public sealed record ActionTally(int Total, int Matches, double Games, double Weight)
{
    public static readonly ActionTally None = new(0, 0, 0, 0);

    /// <summary>Count per standard game; null when the action was never recorded.</summary>
    public double? PerGame => Games > 0 ? Total / Games : null;

    /// <summary>Count per match where it was recorded (what the app shows as "por partida").</summary>
    public double? PerMatch => Matches > 0 ? (double)Total / Matches : null;
}

/// <summary>
/// A player's recorded actions over their history: cortadas, blocks, aces, salvadas and
/// assistências. Matches without a sheet are not in here at all (not as zeros).
/// </summary>
public sealed record PlayerActionTotals(
    int Matches,
    ActionTally Kills,
    ActionTally Blocks,
    ActionTally Aces,
    ActionTally Digs,
    ActionTally Assists)
{
    public static readonly PlayerActionTotals None =
        new(0, ActionTally.None, ActionTally.None, ActionTally.None, ActionTally.None, ActionTally.None);
}
