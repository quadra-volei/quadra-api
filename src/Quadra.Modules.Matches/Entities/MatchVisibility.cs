namespace Quadra.Modules.Matches.Entities;

/// <summary>Who can find and join a match (frontend S11 "Privacidade").</summary>
public enum MatchVisibility
{
    /// <summary>Listed on the map/explore; any player can join.</summary>
    Open,

    /// <summary>Not listed; joining depends on <see cref="MatchInviteMode"/>.</summary>
    Private,
}

/// <summary>How players get into a <see cref="MatchVisibility.Private"/> match.</summary>
public enum MatchInviteMode
{
    /// <summary>Anyone holding the match invite code can join.</summary>
    Code,

    /// <summary>Only players the organizer adds can join.</summary>
    Guests,
}

/// <summary>Skill level a match is aimed at (frontend S11 "Nível").</summary>
public enum MatchLevel
{
    Beginner,
    Intermediate,
    Advanced,
}
