using Quadra.Modules.Gamification.Persistence;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// <c>SeasonPass</c> settings (DECISIONS #98): the dates of each season, by code, and whether the paid
/// track is open to every player while there is no billing.
/// </summary>
public sealed class SeasonPassOptions
{
    public const string SectionName = "SeasonPass";

    /// <summary>Opens the paid track (the card pieces and the card's finish) to everyone: the closed test has no billing.</summary>
    public bool PremiumForEveryone { get; set; }

    /// <summary>By code of <see cref="SeasonPassRules.Catalog"/>. A season in the catalog without dates is not shown.</summary>
    public Dictionary<string, SeasonDates> Seasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every code is in the catalog, ends after it starts and the seasons do not overlap.</summary>
    public bool IsValid()
    {
        if (Seasons.Any(s => !SeasonPassRules.Catalog.ContainsKey(s.Key) || s.Value.EndsOn <= s.Value.StartsOn))
        {
            return false;
        }

        var ordered = Seasons.Values.OrderBy(d => d.StartsOn).ToList();
        return ordered.Zip(ordered.Skip(1)).All(pair => pair.Second.StartsOn >= pair.First.EndsOn);
    }
}

/// <summary>A season's first day and the day after its last one, in Brasília.</summary>
public sealed class SeasonDates
{
    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }
}

/// <summary>What a season gives besides a card piece per level: the finish of the whole card and the free titles by level.</summary>
public sealed record SeasonDefinition(string Code, string Name, string Finish, IReadOnlyDictionary<int, string> Titles);

/// <summary>A season with its dates: from <see cref="StartsAt"/> until, not including, <see cref="EndsAt"/>.</summary>
public sealed record Season(SeasonDefinition Definition, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>One reward: <see cref="Track"/> Free or Premium; <see cref="Kind"/> Piece, Title or Finish.</summary>
public sealed record SeasonReward(int Level, string Track, string Kind, string Code);

/// <summary>The points that count (after the weekly cap), the level they reach and the week being played.</summary>
public sealed record SeasonProgress(int Points, int Level, int WeekPoints, DateTimeOffset? WeekEndsAt);

/// <summary>
/// The season pass (DECISIONS #98). Progress is the player's ranking points (the ledger, #95) from the
/// season's recurring matches, at most <see cref="WeeklyCap"/> per Brasília week (Monday to Sunday), so
/// nobody completes it in a fortnight. Every <see cref="PointsPerLevel"/> points is a level. Each level
/// gives a piece of the season's card on the paid track; the last one also gives the finish of the
/// whole card; some give a card title on the free track. Someone who plays once a week (about 25 points
/// a match) completes the 12 levels in about 10 of the 13 weeks.
/// </summary>
public static class SeasonPassRules
{
    public const int Levels = 12;
    public const int PointsPerLevel = 20;
    public const int WeeklyCap = 60;

    public const string Free = "Free";
    public const string Premium = "Premium";

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>The seasons the app has art for, by code; their dates come from <see cref="SeasonPassOptions"/>.</summary>
    public static readonly IReadOnlyDictionary<string, SeasonDefinition> Catalog = new Dictionary<string, SeasonDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        ["T1"] = new("T1", "Temporada 1", "Pioneira", new Dictionary<int, string>
        {
            [3] = "SEMPRE_PRESENTE",
            [6] = "CRIA_DA_QUADRA",
            [9] = "LENDA_T1",
        }),
    };

    /// <summary>Every season's card titles and finishes: only a pass gives them.</summary>
    public static readonly IReadOnlySet<string> Titles = Catalog.Values.SelectMany(s => s.Titles.Values).ToHashSet(StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> Finishes = Catalog.Values.Select(s => s.Finish).ToHashSet(StringComparer.Ordinal);

    /// <summary>A season's rewards by level: a piece per level, its titles, and the finish at the last level.</summary>
    public static IReadOnlyList<SeasonReward> Rewards(SeasonDefinition season)
    {
        var rewards = new List<SeasonReward>();
        for (var level = 1; level <= Levels; level++)
        {
            rewards.Add(new SeasonReward(level, Premium, "Piece", $"PIECE_{level}"));
            if (season.Titles.TryGetValue(level, out var title))
            {
                rewards.Add(new SeasonReward(level, Free, "Title", title));
            }
        }

        rewards.Add(new SeasonReward(Levels, Premium, "Finish", season.Finish));
        return rewards;
    }

    /// <summary>Whether a reward is the player's: the level reached and, on the paid track, the pass.</summary>
    public static bool IsUnlocked(SeasonReward reward, SeasonProgress progress, bool premium) =>
        progress.Level >= reward.Level && (reward.Track == Free || premium);

    /// <summary>
    /// The season's progress from the points of the player's matches (any period: only the season's
    /// dates count). A week's points beyond the cap are dropped; a later week is not affected.
    /// </summary>
    public static SeasonProgress Progress(Season season, IEnumerable<DatedPoints> points, DateTimeOffset now)
    {
        var weeks = points
            .Where(p => p.MatchDateTime >= season.StartsAt && p.MatchDateTime < season.EndsAt)
            .GroupBy(p => WeekStart(p.MatchDateTime))
            .ToDictionary(g => g.Key, g => Math.Min(g.Sum(p => p.Points), WeeklyCap));
        var total = Math.Max(weeks.Values.Sum(), 0);
        var level = Math.Min(total / PointsPerLevel, Levels);
        if (now < season.StartsAt || now >= season.EndsAt)
        {
            return new SeasonProgress(total, level, 0, null);
        }

        var week = WeekStart(now);
        var next = Local(week.DateTime.AddDays(7));
        return new SeasonProgress(total, level, Math.Max(weeks.GetValueOrDefault(week), 0), next < season.EndsAt ? next : season.EndsAt);
    }

    /// <summary>00:00 of the Monday that starts the Brasília week of <paramref name="moment"/>.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset moment)
    {
        var day = TimeZoneInfo.ConvertTime(moment, Brasilia).Date;
        return Local(day.AddDays(-(((int)day.DayOfWeek + 6) % 7)));
    }

    /// <summary>The configured seasons, in date order.</summary>
    public static IReadOnlyList<Season> Seasons(SeasonPassOptions options) =>
        options.Seasons
            .Where(s => Catalog.ContainsKey(s.Key))
            .Select(s => new Season(
                Catalog[s.Key],
                Local(s.Value.StartsOn.ToDateTime(TimeOnly.MinValue)),
                Local(s.Value.EndsOn.ToDateTime(TimeOnly.MinValue))))
            .OrderBy(s => s.StartsAt)
            .ToList();

    /// <summary>The season to show: the one being played, else the next one, else the last one that ended.</summary>
    public static Season? Current(IReadOnlyList<Season> seasons, DateTimeOffset now) =>
        seasons.FirstOrDefault(s => now >= s.StartsAt && now < s.EndsAt)
        ?? seasons.FirstOrDefault(s => now < s.StartsAt)
        ?? seasons.LastOrDefault();

    private static DateTimeOffset Local(DateTime brasiliaTime) => new(brasiliaTime, Brasilia.GetUtcOffset(brasiliaTime));
}
