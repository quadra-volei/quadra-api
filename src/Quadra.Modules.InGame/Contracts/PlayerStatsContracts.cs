namespace Quadra.Modules.InGame.Contracts;

/// <summary>One player's actions in a match: cortadas, blocks, aces, salvadas, assistências.</summary>
public sealed record PlayerActionsDto(
    Guid PlayerId,
    int Kills,
    int Blocks,
    int Aces,
    int Digs,
    int Assists);

/// <summary>
/// <c>PUT /matches/{id}/player-stats</c>: the whole sheet. A player left out has nothing
/// recorded (not zero); their previous row, if any, is removed.
/// </summary>
public sealed record SavePlayerStatsRequest(IReadOnlyList<PlayerActionsDto> Players);

/// <summary>The recorded sheet of a match and whether the organizer can still change it.</summary>
public sealed record PlayerStatsResponse(
    Guid MatchId,
    bool Editable,
    IReadOnlyList<PlayerActionsDto> Players);
