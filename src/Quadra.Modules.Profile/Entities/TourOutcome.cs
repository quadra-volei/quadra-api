namespace Quadra.Modules.Profile.Entities;

/// <summary>How a player left the app's first-access tour.</summary>
public enum TourOutcome
{
    /// <summary>Went through every step.</summary>
    Completed,

    /// <summary>Left before the last step ("Pular" or "Agora não").</summary>
    Skipped,
}
