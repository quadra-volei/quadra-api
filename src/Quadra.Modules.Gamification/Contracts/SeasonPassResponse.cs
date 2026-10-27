namespace Quadra.Modules.Gamification.Contracts;

/// <summary>
/// <c>GET /api/v1/season-pass</c> (DECISIONS #98): the caller's season pass for the season being
/// played, else the next one, else the last one that ended.
/// </summary>
public sealed record SeasonPassResponse(
    string Season,              // code, e.g. "T1": the app's art is keyed by it
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,      // the season is over at this moment
    string Status,              // Upcoming | Active | Ended
    int Points,                 // the points that count, after the weekly cap
    int Level,                  // 0 to Levels: one card piece per level
    int Levels,
    int PointsPerLevel,
    int WeeklyCap,
    int WeekPoints,             // this week's points that count; 0 while the season is not being played
    DateTimeOffset? WeekEndsAt, // when the weekly cap starts over; null while the season is not being played
    bool Premium,               // the paid track is open to the caller
    IReadOnlyList<SeasonRewardResponse> Rewards);

/// <param name="Track">Free or Premium.</param>
/// <param name="Kind">Piece (of the season's card), Title (a card title code) or Finish (a card finish code).</param>
public sealed record SeasonRewardResponse(int Level, string Track, string Kind, string Code, bool Unlocked);
