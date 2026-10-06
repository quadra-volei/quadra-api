namespace Quadra.Modules.Profile.Contracts;

/// <summary>One entry in a player's paginated match history.</summary>
public sealed record MatchHistoryEntryResponse(
    Guid MatchId,
    string MatchName,
    DateTimeOffset MatchDateTime,
    Guid? TeamId,
    string Outcome,
    bool WasMvp,
    int? DurationSeconds);
