using Microsoft.Extensions.Options;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// The caller's season pass (<c>GET /api/v1/season-pass</c>) and, for Profile, whether a card finish or
/// title a pass gives is the player's (DECISIONS #98). Progress is computed from the ledger on every
/// read: nothing is stored, so a corrected ledger row corrects the pass too.
/// </summary>
public sealed class SeasonPassHandler : ISeasonRewardReader
{
    private readonly IPointTransactionRepository _points;
    private readonly IPremiumStatusReader _premium;
    private readonly SeasonPassOptions _options;
    private readonly TimeProvider _clock;

    public SeasonPassHandler(
        IPointTransactionRepository points,
        IPremiumStatusReader premium,
        IOptions<SeasonPassOptions> options,
        TimeProvider clock)
    {
        _points = points;
        _premium = premium;
        _options = options.Value;
        _clock = clock;
    }

    /// <returns>The pass, or null when no season is set up.</returns>
    public async Task<SeasonPassResponse?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        if (SeasonPassRules.Current(SeasonPassRules.Seasons(_options), now) is not { } season)
        {
            return null;
        }

        var progress = await ProgressAsync(userId, season, now, cancellationToken);
        var premium = await IsPremiumAsync(userId, cancellationToken);
        return new SeasonPassResponse(
            season.Definition.Code,
            season.Definition.Name,
            season.StartsAt,
            season.EndsAt,
            SeasonPassRules.Status(season, now),
            progress.Points,
            progress.Level,
            SeasonPassRules.Levels,
            SeasonPassRules.PointsPerLevel,
            SeasonPassRules.WeeklyCap,
            progress.WeekPoints,
            progress.WeekEndsAt,
            premium,
            SeasonPassRules.Rewards(season.Definition)
                .Select(r => new SeasonRewardResponse(r.Level, r.Track, r.Kind, r.Code, SeasonPassRules.IsUnlocked(r, progress, premium)))
                .ToList());
    }

    /// <summary>
    /// A player's card collection (DECISIONS #99): the card of every season set up, newest first, with
    /// the pieces won and whether it is complete. A season still to come has no piece yet.
    /// </summary>
    public async Task<CardCollectionResponse> GetCollectionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var premium = await IsPremiumAsync(userId, cancellationToken);
        var cards = new List<CollectionCardResponse>();
        foreach (var season in SeasonPassRules.Seasons(_options).OrderByDescending(s => s.StartsAt))
        {
            var progress = await ProgressAsync(userId, season, now, cancellationToken);
            var rewards = SeasonPassRules.Rewards(season.Definition);
            cards.Add(new CollectionCardResponse(
                season.Definition.Code,
                season.Definition.Name,
                season.Definition.Finish,
                season.StartsAt,
                season.EndsAt,
                SeasonPassRules.Status(season, now),
                rewards.Count(r => r.Kind == "Piece" && SeasonPassRules.IsUnlocked(r, progress, premium)),
                SeasonPassRules.Levels,
                rewards.Any(r => r.Kind == "Finish" && SeasonPassRules.IsUnlocked(r, progress, premium))));
        }

        return new CardCollectionResponse(userId, cards);
    }

    /// <inheritdoc/>
    public IReadOnlySet<string> SeasonTitles => SeasonPassRules.Titles;

    /// <inheritdoc/>
    public IReadOnlySet<string> SeasonFinishes => SeasonPassRules.Finishes;

    /// <inheritdoc/>
    public async Task<bool> HasUnlockedAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        foreach (var season in SeasonPassRules.Seasons(_options))
        {
            var reward = SeasonPassRules.Rewards(season.Definition).FirstOrDefault(r => r.Kind != "Piece" && r.Code == code);
            if (reward is not null)
            {
                var progress = await ProgressAsync(userId, season, now, cancellationToken);
                return SeasonPassRules.IsUnlocked(reward, progress, await IsPremiumAsync(userId, cancellationToken));
            }
        }

        return false;
    }

    private async Task<SeasonProgress> ProgressAsync(Guid userId, Season season, DateTimeOffset now, CancellationToken cancellationToken) =>
        SeasonPassRules.Progress(season, await _points.GetPointsAsync(userId, season.StartsAt, season.EndsAt, cancellationToken), now);

    private async Task<bool> IsPremiumAsync(Guid userId, CancellationToken cancellationToken) =>
        _options.PremiumForEveryone || await _premium.IsPremiumAsync(userId, cancellationToken);
}
