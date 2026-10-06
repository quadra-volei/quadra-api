using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Realtime;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for what the mobile in-game flow added on top of F1.3/F1.4: drawing 2 to 4 teams
/// (with guests, a per-team cap and a random mode), sets played by a pair of teams the
/// organizer picks, undoing the last point and ending a set early.
/// </summary>
public sealed class TeamRotationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid MatchId = Guid.NewGuid();
    private static readonly Guid TeamA = Guid.NewGuid();
    private static readonly Guid TeamB = Guid.NewGuid();
    private static readonly Guid TeamC = Guid.NewGuid();

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IConfirmedPlayersReader _confirmedPlayers = Substitute.For<IConfirmedPlayersReader>();
    private readonly IPlayerLevelReader _levels = Substitute.For<IPlayerLevelReader>();
    private readonly ITeamRepository _teams = Substitute.For<ITeamRepository>();
    private readonly IScoreboardRepository _scoreboards = Substitute.For<IScoreboardRepository>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();
    private readonly IMatchRoomNotifier _notifier = Substitute.For<IMatchRoomNotifier>();
    private readonly TimeProvider _clock = new FixedTimeProvider(Now);

    public TeamRotationTests()
    {
        _matchReader.FindMatchSummaryAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(MatchId, Organizer, "Open"));
        _levels.GetPlayerLevelsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _confirmedPlayers.GetGuestIdsAsync(MatchId, Arg.Any<CancellationToken>()).Returns([]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ═══ draft ════════════════════════════════════════════════════════════════

    private DraftTeamsHandler Draft() => new(
        _matchReader, _confirmedPlayers, _levels, _teams, _events, _clock, NullLogger<DraftTeamsHandler>.Instance);

    private void Confirmed(params Guid[] ids) =>
        _confirmedPlayers.GetConfirmedPlayerIdsAsync(MatchId, Arg.Any<CancellationToken>()).Returns(ids);

    private static Guid[] Players(int count) =>
        Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

    /// <summary>
    /// Covers: three teams, balanced — the snake (A,B,C,C,B,A,A,…) spreads the best players so
    /// no team holds the first pick of every round. Teams may be drawn while the window is Open.
    /// </summary>
    [Fact]
    public async Task Draft_deals_three_teams_in_a_snake_by_level()
    {
        var players = Players(7);
        Confirmed(players);
        // Levels descending in id order, so the sorted order is the id order.
        _levels.GetPlayerLevelsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>
            {
                [players[0]] = "Elite", [players[1]] = "Elite", [players[2]] = "Advanced",
                [players[3]] = "Advanced", [players[4]] = "Intermediate", [players[5]] = "Intermediate",
            });

        var response = await Draft().HandleAsync(MatchId, Organizer, new DraftTeamsRequest(TeamCount: 3), Ct);

        response.Teams.Select(t => t.Name).Should().Equal("Team A", "Team B", "Team C");
        response.Teams[0].Members.Select(m => m.PlayerId).Should().Equal(players[0], players[5], players[6]);
        response.Teams[1].Members.Select(m => m.PlayerId).Should().Equal(players[1], players[4]);
        response.Teams[2].Members.Select(m => m.PlayerId).Should().Equal(players[2], players[3]);
    }

    /// <summary>
    /// Covers: guests play in the teams (flagged, counted as Beginner) but are left out of the
    /// TeamsFormed event, whose ids are users.
    /// </summary>
    [Fact]
    public async Task Draft_includes_guests_in_teams_but_not_in_the_event()
    {
        var players = Players(3);
        var guest = Guid.NewGuid();
        Confirmed(players);
        _confirmedPlayers.GetGuestIdsAsync(MatchId, Arg.Any<CancellationToken>()).Returns([guest]);

        var response = await Draft().HandleAsync(MatchId, Organizer, new DraftTeamsRequest(), Ct);

        var members = response.Teams.SelectMany(t => t.Members).ToList();
        members.Should().HaveCount(4);
        members.Single(m => m.IsGuest).PlayerId.Should().Be(guest);
        await _events.Received(1).PublishAsync(
            Arg.Is<TeamsFormed>(e => e.Teams.SelectMany(t => t.PlayerIds).Count() == 3
                && !e.Teams.SelectMany(t => t.PlayerIds).Contains(guest)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: a per-team cap leaves the extra players out, and Random mode still fills every
    /// team evenly with distinct players.
    /// </summary>
    [Fact]
    public async Task Draft_caps_team_size_and_supports_random_mode()
    {
        var players = Players(11);
        Confirmed(players);

        var response = await Draft().HandleAsync(
            MatchId, Organizer, new DraftTeamsRequest(TeamCount: 4, PerTeam: 2, Mode: "Random"), Ct);

        response.Teams.Should().HaveCount(4).And.OnlyContain(t => t.Members.Count == 2);
        response.Teams.SelectMany(t => t.Members).Select(m => m.PlayerId)
            .Should().OnlyHaveUniqueItems().And.BeSubsetOf(players);
    }

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(5, null, null)]
    [InlineData(2, 0, null)]
    [InlineData(2, null, "Alphabetical")]
    public async Task Draft_rejects_invalid_options(int teamCount, int? perTeam, string? mode)
    {
        Confirmed(Players(4));

        var act = async () => await Draft().HandleAsync(
            MatchId, Organizer, new DraftTeamsRequest(teamCount, perTeam, mode), Ct);

        await act.Should().ThrowAsync<InvalidDraftOptionsException>();
    }

    // ═══ scoreboard with rotating teams ═══════════════════════════════════════

    private Scoreboard Rotating(ScoreboardFormat format = ScoreboardFormat.BestOf3)
    {
        var scoreboard = Scoreboard.Create(MatchId, format, TeamA, TeamB, Now, rotatesTeams: true);
        scoreboard.Start(Now);
        scoreboard.OpenSet(1, isDecidingSet: false, Now);
        _scoreboards.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(scoreboard);
        _teams.ListByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(
        [
            TeamWithId(TeamA, "Team A"), TeamWithId(TeamB, "Team B"), TeamWithId(TeamC, "Team C"),
        ]);
        return scoreboard;
    }

    private static Team TeamWithId(Guid id, string name)
    {
        var team = Team.Create(MatchId, name, Now);
        typeof(Team).GetProperty(nameof(Team.Id))!.SetValue(team, id);
        return team;
    }

    private RecordPointHandler Points() => new(
        _matchReader, _scoreboards, _events, _notifier, _clock, NullLogger<RecordPointHandler>.Instance);

    private EndSetHandler EndSet() => new(
        _matchReader, _scoreboards, _events, _notifier, _clock, NullLogger<EndSetHandler>.Instance);

    private UndoPointHandler Undo() => new(_matchReader, _scoreboards, _notifier, _clock);

    private OpenNextSetHandler NextSet() => new(_matchReader, _scoreboards, _teams, _notifier, _clock);

    private async Task ScoreAsync(int setNumber, Guid teamId, int points)
    {
        for (var i = 0; i < points; i++)
        {
            await Points().HandleAsync(MatchId, setNumber, teamId, Organizer, Ct);
        }
    }

    /// <summary>
    /// Covers: with more than two teams a finished set does not open the next one — the game
    /// waits for the organizer to say who plays; the picked pair then plays set 2 and each set
    /// records its own pair.
    /// </summary>
    [Fact]
    public async Task Rotating_game_waits_for_the_next_pair_after_a_set()
    {
        var scoreboard = Rotating();
        await ScoreAsync(1, TeamA, 25);

        scoreboard.State.Should().Be(ScoreboardState.InProgress);
        scoreboard.Sets.Should().ContainSingle().Which.Status.Should().Be(SetStatus.Finished);

        var response = await NextSet().HandleAsync(MatchId, TeamA, TeamC, Organizer, Ct);

        response.AwaitingNextSet.Should().BeFalse();
        response.CurrentSetNumber.Should().Be(2);
        response.TeamAId.Should().Be(TeamA);
        response.TeamBId.Should().Be(TeamC);
        response.TeamASetsWon.Should().Be(1, "Team A carries the set it already won");
        response.TeamBSetsWon.Should().Be(0);
        response.Sets[0].TeamBId.Should().Be(TeamB);
        response.Sets[1].TeamBId.Should().Be(TeamC);
        response.Sets[1].IsDecidingSet.Should().BeFalse();
    }

    /// <summary>
    /// Covers: the game ends when one team reaches the sets the format asks for, whichever
    /// opponents it beat; a set between two teams one win away is the deciding (15-point) set.
    /// </summary>
    [Fact]
    public async Task Rotating_game_ends_when_a_team_wins_enough_sets()
    {
        var scoreboard = Rotating();
        await ScoreAsync(1, TeamA, 25);                                  // A beats B
        await NextSet().HandleAsync(MatchId, TeamA, TeamC, Organizer, Ct);
        await ScoreAsync(2, TeamC, 25);                                  // C beats A
        var third = await NextSet().HandleAsync(MatchId, TeamC, TeamA, Organizer, Ct);
        third.Sets[2].IsDecidingSet.Should().BeTrue("A and C both have one set in a best of 3");

        await ScoreAsync(3, TeamC, 15);                                  // C beats A again, to 15

        scoreboard.State.Should().Be(ScoreboardState.Ended);
        scoreboard.WinnerTeamId.Should().Be(TeamC);
        await _events.Received(1).PublishAsync(
            Arg.Is<MatchEnded>(e => e.WinnerTeamId == TeamC), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Next_set_needs_two_different_teams_of_the_match_and_no_open_set()
    {
        Rotating();

        var whileOpen = async () => await NextSet().HandleAsync(MatchId, TeamA, TeamC, Organizer, Ct);
        await whileOpen.Should().ThrowAsync<InvalidScoreboardStateException>();

        await ScoreAsync(1, TeamA, 25);
        var sameTeam = async () => await NextSet().HandleAsync(MatchId, TeamA, TeamA, Organizer, Ct);
        var stranger = async () => await NextSet().HandleAsync(MatchId, TeamA, Guid.NewGuid(), Organizer, Ct);
        await sameTeam.Should().ThrowAsync<TeamNotInScoreboardException>();
        await stranger.Should().ThrowAsync<TeamNotInScoreboardException>();
    }

    // ═══ undo + early end ═════════════════════════════════════════════════════

    /// <summary>
    /// Covers: undo takes back the last point only (one level), and only the organizer may.
    /// </summary>
    [Fact]
    public async Task Undo_takes_back_only_the_last_point()
    {
        Rotating();
        await ScoreAsync(1, TeamA, 2);
        await ScoreAsync(1, TeamB, 1);

        var response = await Undo().HandleAsync(MatchId, 1, Organizer, Ct);

        response.Sets[0].TeamAPoints.Should().Be(2);
        response.Sets[0].TeamBPoints.Should().Be(0);
        response.Sets[0].CanUndo.Should().BeFalse();
        var again = async () => await Undo().HandleAsync(MatchId, 1, Organizer, Ct);
        await again.Should().ThrowAsync<InvalidScoreboardStateException>();
        var stranger = async () => await Undo().HandleAsync(MatchId, 1, Guid.NewGuid(), Ct);
        await stranger.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    /// <summary>
    /// Covers: ending a set early gives it to whoever is ahead; a tied set cannot be ended. In a
    /// two-team game the next set opens by itself.
    /// </summary>
    [Fact]
    public async Task Ending_a_set_early_gives_it_to_the_leader()
    {
        var scoreboard = Scoreboard.Create(MatchId, ScoreboardFormat.BestOf3, TeamA, TeamB, Now);
        scoreboard.Start(Now);
        scoreboard.OpenSet(1, isDecidingSet: false, Now);
        _scoreboards.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var tied = async () => await EndSet().HandleAsync(MatchId, 1, Organizer, Ct);
        await tied.Should().ThrowAsync<InvalidScoreboardStateException>();

        await ScoreAsync(1, TeamB, 3);
        var response = await EndSet().HandleAsync(MatchId, 1, Organizer, Ct);

        response.Sets[0].WinnerTeamId.Should().Be(TeamB);
        response.TeamBSetsWon.Should().Be(1);
        response.CurrentSetNumber.Should().Be(2);
        response.Sets[1].Status.Should().Be("InProgress");
        await _events.DidNotReceive().PublishAsync(Arg.Any<MatchEnded>(), Arg.Any<CancellationToken>());

        // The second early end gives Team B the game.
        await ScoreAsync(2, TeamB, 1);
        var final = await EndSet().HandleAsync(MatchId, 2, Organizer, Ct);
        final.State.Should().Be("Ended");
        final.WinnerTeamId.Should().Be(TeamB);
        await _events.Received(1).PublishAsync(Arg.Any<MatchEnded>(), Arg.Any<CancellationToken>());
    }
}
