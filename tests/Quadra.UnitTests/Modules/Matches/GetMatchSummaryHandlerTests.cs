using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;
using MatchResult = Quadra.Shared.Contracts.MatchResult;
using MatchSummaryEntity = Quadra.Modules.Matches.Entities.MatchSummary;
using MatchSummaryProjection = Quadra.Shared.Contracts.MatchSummary;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="GetMatchSummaryHandler"/> (F1.6 — GET /matches/{id}/summary),
/// including the shared entity → DTO mapping (ordering of sets, teams and player ids).
/// </summary>
public sealed class GetMatchSummaryHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 7, 1, 11, 0, 0, TimeSpan.Zero);
    private static readonly Guid TeamA = new("0000000a-0000-0000-0000-000000000000");
    private static readonly Guid TeamB = new("0000000b-0000-0000-0000-000000000000");
    private static readonly Guid P1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid P4 = new("44444444-4444-4444-4444-444444444444");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IMatchSummaryRepository _summaryRepository = Substitute.For<IMatchSummaryRepository>();

    private GetMatchSummaryHandler CreateSut() => new(_matchReader, _summaryRepository);

    // ─── 404: match not found ─────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 GET step 1 — 404 when the match does not exist.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_match_not_found_throws_MatchNotFoundException()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns((MatchSummaryProjection?)null);

        var act = async () => await CreateSut().HandleAsync(matchId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    // ─── 404: no summary yet ──────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 GET step 2 — 404 when the match exists but no summary has been generated.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_no_summary_throws_MatchSummaryNotFoundException()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummaryProjection(matchId, Guid.NewGuid(), "Ended"));
        _summaryRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchSummaryNotFoundException>();
    }

    // ─── 200: mapping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 AC "endpoint returning final score, duration, MVP, team composition" +
    /// GET step 3 — the summary maps to the response with sets ordered by set number, teams ordered
    /// by name and player ids ordered ascending.
    /// </summary>
    [Fact]
    public async Task HandleAsync_maps_summary_with_ordered_sets_teams_and_players()
    {
        var matchId = Guid.NewGuid();
        var summary = BuildSummary(matchId);
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummaryProjection(matchId, Guid.NewGuid(), "Ended"));
        _summaryRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(summary);

        var response = await CreateSut().HandleAsync(matchId, CancellationToken.None);

        response.MatchId.Should().Be(matchId);
        response.Format.Should().Be("BestOf3");
        response.TeamASetsWon.Should().Be(2);
        response.TeamBSetsWon.Should().Be(1);
        response.WinnerTeamId.Should().Be(TeamA);
        response.DurationSeconds.Should().Be(3600);
        response.MvpPlayerId.Should().Be(P1);
        response.MvpVoteCount.Should().Be(3);
        response.MvpTotalVotes.Should().Be(4);

        response.Sets.Select(s => s.SetNumber).Should().ContainInOrder(1, 2, 3);
        response.Teams.Select(t => t.Name).Should().ContainInOrder("Team A", "Team B");
        response.Teams[0].PlayerIds.Should().ContainInOrder(P1, P2);
        response.Teams[1].PlayerIds.Should().ContainInOrder(P3, P4);
    }

    private static MatchSummaryEntity BuildSummary(Guid matchId)
    {
        var result = new MatchResult(
            MatchId: matchId,
            ScoreboardState: "Ended",
            Format: "BestOf3",
            TeamAId: TeamA,
            TeamBId: TeamB,
            TeamASetsWon: 2,
            TeamBSetsWon: 1,
            WinnerTeamId: TeamA,
            StartedAt: Start,
            EndedAt: End,
            Sets:
            [
                new MatchResultSet(3, 25, 23, TeamA),
                new MatchResultSet(1, 25, 20, TeamA),
                new MatchResultSet(2, 18, 25, TeamB),
            ],
            Teams:
            [
                new MatchResultTeam(TeamB, "Team B", [P4, P3]),
                new MatchResultTeam(TeamA, "Team A", [P2, P1]),
            ],
            MvpVotingState: "Closed",
            MvpPlayerId: P1,
            MvpVoteCount: 3,
            MvpTotalVotes: 4);

        return MatchSummaryEntity.Generate(matchId, result, generatedBy: Guid.NewGuid(), now: End);
    }
}
