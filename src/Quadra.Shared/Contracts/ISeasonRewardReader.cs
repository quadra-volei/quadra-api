namespace Quadra.Shared.Contracts;

/// <summary>
/// The card cosmetics only a season pass gives (a card finish, card titles), for the module that
/// lets a player equip them. Declared in <c>Quadra.Shared</c>; implemented by Gamification, which
/// owns the pass (DECISIONS #98).
/// </summary>
public interface ISeasonRewardReader
{
    /// <summary>The card title codes the season passes give, of any season.</summary>
    IReadOnlySet<string> SeasonTitles { get; }

    /// <summary>The card finish codes the season passes give, of any season.</summary>
    IReadOnlySet<string> SeasonFinishes { get; }

    /// <summary>Whether the player unlocked that title or finish in its season; it stays theirs after the season ends.</summary>
    Task<bool> HasUnlockedAsync(Guid userId, string code, CancellationToken cancellationToken);
}
