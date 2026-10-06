using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Retrieves the full current scoreboard state (including all sets) for a match.
/// Also hosts the shared entity-to-DTO and entity-to-realtime mapping helpers reused by the
/// mutating scoreboard handlers.
/// </summary>
public sealed class GetScoreboardHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboardRepository;

    public GetScoreboardHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository)
    {
        _matchReader = matchReader;
        _scoreboardRepository = scoreboardRepository;
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        _ = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Load scoreboard (with sets).
        var scoreboard = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new ScoreboardNotFoundException();

        // 3. Map and return.
        return MapToResponse(scoreboard);
    }

    /// <summary>
    /// Maps a <see cref="Scoreboard"/> aggregate to its response DTO (sets ordered by set number).
    /// </summary>
    internal static ScoreboardResponse MapToResponse(Scoreboard scoreboard)
    {
        return new ScoreboardResponse(
            Id: scoreboard.Id,
            MatchId: scoreboard.MatchId,
            Format: scoreboard.Format.ToString(),
            State: scoreboard.State.ToString(),
            TeamAId: scoreboard.TeamAId,
            TeamBId: scoreboard.TeamBId,
            TeamASetsWon: scoreboard.TeamASetsWon,
            TeamBSetsWon: scoreboard.TeamBSetsWon,
            CurrentSetNumber: scoreboard.CurrentSetNumber,
            WinnerTeamId: scoreboard.WinnerTeamId,
            Sets: scoreboard.Sets
                .OrderBy(s => s.SetNumber)
                .Select(s => new ScoreboardSetResponse(
                    Id: s.Id,
                    SetNumber: s.SetNumber,
                    TeamAPoints: s.TeamAPoints,
                    TeamBPoints: s.TeamBPoints,
                    Status: s.Status.ToString(),
                    IsDecidingSet: s.IsDecidingSet,
                    WinnerTeamId: s.WinnerTeamId,
                    StartedAt: s.StartedAt,
                    FinishedAt: s.FinishedAt))
                .ToList(),
            StartedAt: scoreboard.StartedAt,
            EndedAt: scoreboard.EndedAt,
            CreatedAt: scoreboard.CreatedAt,
            UpdatedAt: scoreboard.UpdatedAt);
    }

    /// <summary>
    /// Builds the SignalR broadcast payload for the current scoreboard state.
    /// The current-set point totals come from the set matching <see cref="Scoreboard.CurrentSetNumber"/>.
    /// </summary>
    internal static ScoreboardUpdatedMessage ToRealtimeMessage(Scoreboard scoreboard, DateTimeOffset occurredAt)
    {
        var currentSet = scoreboard.Sets.FirstOrDefault(s => s.SetNumber == scoreboard.CurrentSetNumber);

        return new ScoreboardUpdatedMessage(
            MatchId: scoreboard.MatchId,
            State: scoreboard.State.ToString(),
            Format: scoreboard.Format.ToString(),
            TeamAId: scoreboard.TeamAId,
            TeamBId: scoreboard.TeamBId,
            TeamASetsWon: scoreboard.TeamASetsWon,
            TeamBSetsWon: scoreboard.TeamBSetsWon,
            CurrentSetNumber: scoreboard.CurrentSetNumber,
            CurrentSetTeamAPoints: currentSet?.TeamAPoints ?? 0,
            CurrentSetTeamBPoints: currentSet?.TeamBPoints ?? 0,
            WinnerTeamId: scoreboard.WinnerTeamId,
            OccurredAt: occurredAt);
    }
}
