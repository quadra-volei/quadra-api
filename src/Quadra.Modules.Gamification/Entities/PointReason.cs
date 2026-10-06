namespace Quadra.Modules.Gamification.Entities;

/// <summary>
/// The reason a point award was granted for a finished match. Persisted as its string name
/// (max 16 chars) via a value converter. The three MVP reasons are the only ones scored by
/// SCOPE F2.3 (attendance/win/MVP); no streak or DropIn reason exists in the MVP.
/// </summary>
public enum PointReason
{
    Attendance,
    Win,
    Mvp,
}
