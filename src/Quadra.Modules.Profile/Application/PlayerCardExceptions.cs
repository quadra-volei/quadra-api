namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Thrown when a profile exists but its player card has not been generated yet (fewer than
/// <see cref="Threshold"/> recorded matches). Carries the current match count for a helpful body.
/// </summary>
public sealed class CardNotGeneratedException : Exception
{
    public CardNotGeneratedException(int matchesPlayed, int threshold)
        : base($"The player card has not been generated yet ({matchesPlayed}/{threshold} recorded matches).")
    {
        MatchesPlayed = matchesPlayed;
        Threshold = threshold;
    }

    /// <summary>The player's current recorded-match count.</summary>
    public int MatchesPlayed { get; }

    /// <summary>The number of recorded matches required before a card is generated.</summary>
    public int Threshold { get; }
}
