namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// A player's profile. <see cref="OnboardingCompleted"/> is false for the empty shell created at
/// registration; the app sends the player through onboarding until it is true.
/// <see cref="BirthDate"/> and <see cref="PhotoObjectKey"/> are only returned to the profile's owner
/// (the key is what the owner sends back on <c>PUT</c> to keep the current photo), and so is
/// <see cref="Tour"/> (null while the owner never finished or skipped the first-access tour).
/// </summary>
public sealed record PlayerProfileResponse(
    Guid UserId,
    string DisplayName,
    string FirstName,
    string LastName,
    string? Handle,
    DateOnly? BirthDate,
    string? Position,
    string? Modality,
    string? DeclaredLevel,
    string Level,
    string? PhotoUrl,
    string? PhotoObjectKey,
    bool OnboardingCompleted,
    PlayerSkillsResponse Skills,
    PlayerStatsResponse Stats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool? ShareActivity = null,
    Quadra.Modules.Profile.CardStyle.PlayerCardStyleResponse? CardStyle = null,
    PlayerActionsResponse? Actions = null,
    Quadra.Modules.Profile.Tour.PlayerTourResponse? Tour = null);

/// <summary>
/// Skill ratings (1–99) derived from the declared level and position and moved by the recorded
/// actions; <see cref="Overall"/> weighs each rating's movement by position (DECISIONS #85).
/// Not editable.
/// </summary>
public sealed record PlayerSkillsResponse(
    int Overall,
    int Ace,
    int Block,
    int Attack,
    int Defense);

/// <summary>
/// The player's recorded actions: volume (total) and the average per match where the action
/// was recorded. <see cref="Matches"/> counts the matches with a sheet.
/// </summary>
public sealed record PlayerActionsResponse(
    int Matches,
    ActionSummaryResponse Kills,
    ActionSummaryResponse Blocks,
    ActionSummaryResponse Aces,
    ActionSummaryResponse Digs,
    ActionSummaryResponse Assists)
{
    public static PlayerActionsResponse From(Quadra.Modules.Profile.Entities.PlayerActionTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return new(
            totals.Matches,
            ActionSummaryResponse.From(totals.Kills),
            ActionSummaryResponse.From(totals.Blocks),
            ActionSummaryResponse.From(totals.Aces),
            ActionSummaryResponse.From(totals.Digs),
            ActionSummaryResponse.From(totals.Assists));
    }
}

/// <summary>One action: the total and the average per match (one decimal; null when never recorded).</summary>
public sealed record ActionSummaryResponse(int Total, double? PerMatch)
{
    public static ActionSummaryResponse From(Quadra.Modules.Profile.Entities.ActionTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        return new(tally.Total, tally.PerMatch is { } avg ? Math.Round(avg, 1) : null);
    }
}
