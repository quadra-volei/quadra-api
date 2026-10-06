using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

// The entity MatchSummary collides by simple name with the Quadra.Shared.Contracts.MatchSummary
// projection record imported above. Alias fixes the reference unambiguously to the Matches entity.
using MatchSummary = Quadra.Modules.Matches.Entities.MatchSummary;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Retrieves the immutable summary (with its sets and team composition) for a match.
/// Also hosts the shared entity-to-DTO mapping helper reused by <see cref="GenerateMatchSummaryHandler"/>.
/// </summary>
public sealed class GetMatchSummaryHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IMatchSummaryRepository _summaryRepository;

    public GetMatchSummaryHandler(
        IMatchReader matchReader,
        IMatchSummaryRepository summaryRepository)
    {
        _matchReader = matchReader;
        _summaryRepository = summaryRepository;
    }

    public async Task<MatchSummaryResponse> HandleAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        // 1. Verify the match exists.
        _ = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Load the summary (with sets and players).
        var summary = await _summaryRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new MatchSummaryNotFoundException(matchId);

        // 3. Map and return.
        return MapToResponse(summary);
    }

    /// <summary>
    /// Maps a <see cref="MatchSummary"/> aggregate to its response DTO: sets ordered by set number;
    /// players grouped into teams ordered by team name; player IDs ordered ascending.
    /// </summary>
    internal static MatchSummaryResponse MapToResponse(MatchSummary summary)
    {
        var sets = summary.Sets
            .OrderBy(s => s.SetNumber)
            .Select(s => new MatchSummarySetResponse(
                SetNumber: s.SetNumber,
                TeamAPoints: s.TeamAPoints,
                TeamBPoints: s.TeamBPoints,
                WinnerTeamId: s.WinnerTeamId))
            .ToList();

        var teams = summary.Players
            .GroupBy(p => new { p.TeamId, p.TeamName })
            .OrderBy(g => g.Key.TeamName, StringComparer.Ordinal)
            .Select(g => new MatchSummaryTeamResponse(
                TeamId: g.Key.TeamId,
                Name: g.Key.TeamName,
                PlayerIds: g.Select(p => p.PlayerId).OrderBy(id => id).ToList()))
            .ToList();

        return new MatchSummaryResponse(
            Id: summary.Id,
            MatchId: summary.MatchId,
            Format: summary.Format,
            TeamAId: summary.TeamAId,
            TeamBId: summary.TeamBId,
            TeamASetsWon: summary.TeamASetsWon,
            TeamBSetsWon: summary.TeamBSetsWon,
            WinnerTeamId: summary.WinnerTeamId,
            StartedAt: summary.StartedAt,
            EndedAt: summary.EndedAt,
            DurationSeconds: summary.DurationSeconds,
            MvpPlayerId: summary.MvpPlayerId,
            MvpVoteCount: summary.MvpVoteCount,
            MvpTotalVotes: summary.MvpTotalVotes,
            Sets: sets,
            Teams: teams,
            GeneratedAt: summary.GeneratedAt);
    }
}
