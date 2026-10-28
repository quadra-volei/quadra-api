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
    private readonly IMatchResultReader _matchResultReader;

    public GetMatchSummaryHandler(
        IMatchReader matchReader,
        IMatchSummaryRepository summaryRepository,
        IMatchResultReader matchResultReader)
    {
        _matchReader = matchReader;
        _summaryRepository = summaryRepository;
        _matchResultReader = matchResultReader;
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

        // 3. Map and return, with the teams of the game (a team of guests only has no player row).
        var result = await _matchResultReader.GetMatchResultAsync(matchId, cancellationToken);
        return MapToResponse(summary, result?.Teams);
    }

    /// <summary>
    /// Maps a <see cref="MatchSummary"/> aggregate to its response DTO: sets ordered by set number;
    /// players grouped into teams ordered by team name; player IDs ordered ascending. A team made
    /// only of guests (no account) has no player row: it comes from <paramref name="gameTeams"/>,
    /// with no player ids, so the summary never drops a team that won games (the app names and
    /// colors teams by their place in this list, so a missing team shifted every name).
    /// </summary>
    internal static MatchSummaryResponse MapToResponse(MatchSummary summary, IReadOnlyList<MatchResultTeam>? gameTeams = null)
    {
        var sets = summary.Sets
            .OrderBy(s => s.SetNumber)
            .Select(s => new MatchSummarySetResponse(
                SetNumber: s.SetNumber,
                TeamAPoints: s.TeamAPoints,
                TeamBPoints: s.TeamBPoints,
                WinnerTeamId: s.WinnerTeamId))
            .ToList();

        var withPlayers = summary.Players
            .GroupBy(p => new { p.TeamId, p.TeamName })
            .Select(g => new MatchSummaryTeamResponse(
                TeamId: g.Key.TeamId,
                Name: g.Key.TeamName,
                PlayerIds: g.Select(p => p.PlayerId).OrderBy(id => id).ToList()));
        var guestsOnly = (gameTeams ?? [])
            .Where(t => summary.Players.All(p => p.TeamId != t.TeamId))
            .Select(t => new MatchSummaryTeamResponse(t.TeamId, t.Name, []));
        var teams = withPlayers.Concat(guestsOnly)
            .OrderBy(t => TeamPalette.Order(t.Name))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
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
