using FluentAssertions;
using Quadra.Shared.Contracts;
using MatchSummaryEntity = Quadra.Modules.Matches.Entities.MatchSummary;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for the <see cref="MatchSummaryEntity"/> aggregate factory (F1.6 snapshot semantics).
/// The record is immutable — <see cref="MatchSummaryEntity.Generate"/> is the only construction path.
/// </summary>
public sealed class MatchSummaryEntityTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 7, 1, 11, 30, 0, TimeSpan.Zero); // +5400s
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid MatchId = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid TeamA = new("0000000a-0000-0000-0000-000000000000");
    private static readonly Guid TeamB = new("0000000b-0000-0000-0000-000000000000");
    private static readonly Guid P1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid P4 = new("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// Covers: F1.6 "Duration" note — Generate precomputes duration as whole seconds from start/end.
    /// </summary>
    [Fact]
    public void Generate_computes_duration_seconds_from_scoreboard_start_and_end()
    {
        var summary = MatchSummaryEntity.Generate(MatchId, BuildResult(), Organizer, Now);

        summary.DurationSeconds.Should().Be(5400);
        summary.StartedAt.Should().Be(Start);
        summary.EndedAt.Should().Be(End);
    }

    /// <summary>
    /// Covers: F1.6 snapshot semantics — top-level score, format, teams and MVP fields are copied verbatim.
    /// </summary>
    [Fact]
    public void Generate_snapshots_score_format_and_mvp_fields()
    {
        var summary = MatchSummaryEntity.Generate(MatchId, BuildResult(), Organizer, Now);

        summary.MatchId.Should().Be(MatchId);
        summary.Format.Should().Be("BestOf3");
        summary.TeamAId.Should().Be(TeamA);
        summary.TeamBId.Should().Be(TeamB);
        summary.TeamASetsWon.Should().Be(2);
        summary.TeamBSetsWon.Should().Be(1);
        summary.WinnerTeamId.Should().Be(TeamA);
        summary.MvpPlayerId.Should().Be(P1);
        summary.MvpVoteCount.Should().Be(3);
        summary.MvpTotalVotes.Should().Be(4);
        summary.GeneratedBy.Should().Be(Organizer);
        summary.GeneratedAt.Should().Be(Now);
        summary.Id.Should().NotBeEmpty();
    }

    /// <summary>
    /// Covers: F1.6 — one MatchSummarySet child is created per result set with matching points/winner.
    /// </summary>
    [Fact]
    public void Generate_creates_one_set_child_per_result_set()
    {
        var summary = MatchSummaryEntity.Generate(MatchId, BuildResult(), Organizer, Now);

        summary.Sets.Should().HaveCount(3);
        var set2 = summary.Sets.Single(s => s.SetNumber == 2);
        set2.TeamAPoints.Should().Be(18);
        set2.TeamBPoints.Should().Be(25);
        set2.WinnerTeamId.Should().Be(TeamB);
        summary.Sets.Should().OnlyContain(s => s.MatchId == MatchId && s.MatchSummaryId == summary.Id);
    }

    /// <summary>
    /// Covers: F1.6 team composition — one MatchSummaryPlayer child per player across both teams,
    /// carrying the team id and snapshot team name.
    /// </summary>
    [Fact]
    public void Generate_creates_one_player_child_per_player_with_team_snapshot()
    {
        var summary = MatchSummaryEntity.Generate(MatchId, BuildResult(), Organizer, Now);

        summary.Players.Should().HaveCount(4);
        summary.Players.Select(p => p.PlayerId).Should().BeEquivalentTo([P1, P2, P3, P4]);
        summary.Players.Where(p => p.TeamId == TeamA).Select(p => p.PlayerId)
            .Should().BeEquivalentTo([P1, P2]);
        summary.Players.Single(p => p.PlayerId == P3).TeamName.Should().Be("Team B");
    }

    /// <summary>
    /// Covers: F1.6 — a level (equal-sets, forced-end) game snapshots a null winner.
    /// </summary>
    [Fact]
    public void Generate_with_no_winner_keeps_winner_null()
    {
        var summary = MatchSummaryEntity.Generate(
            MatchId, BuildResult() with { WinnerTeamId = null }, Organizer, Now);

        summary.WinnerTeamId.Should().BeNull();
    }

    /// <summary>
    /// Covers: F1.6 — when no MVP was decided, the MVP snapshot fields stay null / zero.
    /// </summary>
    [Fact]
    public void Generate_with_no_mvp_keeps_mvp_fields_null()
    {
        var result = BuildResult() with
        {
            MvpVotingState = "None",
            MvpPlayerId = null,
            MvpVoteCount = null,
            MvpTotalVotes = 0,
        };

        var summary = MatchSummaryEntity.Generate(MatchId, result, Organizer, Now);

        summary.MvpPlayerId.Should().BeNull();
        summary.MvpVoteCount.Should().BeNull();
        summary.MvpTotalVotes.Should().Be(0);
    }

    private static MatchResult BuildResult() =>
        new(
            MatchId: MatchId,
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
                new MatchResultSet(1, 25, 20, TeamA),
                new MatchResultSet(2, 18, 25, TeamB),
                new MatchResultSet(3, 25, 23, TeamA),
            ],
            Teams:
            [
                new MatchResultTeam(TeamA, "Team A", [P1, P2]),
                new MatchResultTeam(TeamB, "Team B", [P3, P4]),
            ],
            MvpVotingState: "Closed",
            MvpPlayerId: P1,
            MvpVoteCount: 3,
            MvpTotalVotes: 4);
}
