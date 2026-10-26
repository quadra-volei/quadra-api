namespace Quadra.Modules.Gamification.Entities;

/// <summary>
/// The reason a point award was granted for a finished match. Persisted as its string name
/// (max 16 chars) via a value converter (DECISIONS #63, #95).
/// </summary>
public enum PointReason
{
    Attendance,

    /// <summary>Win of the whole match; only on matches scored before DECISIONS #95.</summary>
    Win,
    Mvp,

    /// <summary>Games won in the match; <c>quantity</c> holds how many.</summary>
    GameWin,

    /// <summary>MVP votes received; <c>quantity</c> holds how many.</summary>
    MvpVote,
}
