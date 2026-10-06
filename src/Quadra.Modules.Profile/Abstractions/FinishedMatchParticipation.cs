namespace Quadra.Modules.Profile.Abstractions;

/// <summary>
/// Self-contained description of one player's participation in one finished match, composed by the
/// Background Worker (from cross-module reads) and handed to the Profile module. Profile never
/// reaches into <c>Matches</c>/<c>InGame</c> tables — it only receives this DTO.
/// </summary>
public sealed record FinishedMatchParticipation(
    Guid PlayerId,
    Guid MatchId,
    string MatchName,
    DateTimeOffset MatchDateTime,
    Guid? TeamId,
    string Outcome,
    bool WasMvp,
    int? DurationSeconds,
    DateTimeOffset FinishedAt);
