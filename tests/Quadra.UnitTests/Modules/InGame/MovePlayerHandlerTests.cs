using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="MovePlayerHandler"/>.
///
/// Covers:
///   - F1.3 Criterion: PUT moves player to specified team and returns updated composition
///   - F1.3 Criterion: 403 when caller is not the organizer
///   - F1.3 Criterion: 404 when match not found
///   - F1.3 Criterion: 409 when match status is not Closed
///   - F1.3 Criterion: 404 when teams not yet formed
///   - F1.3 Criterion: 404 when teamId not found for this match
///   - F1.3 Criterion: 404 when player not in any team
///   - F1.3 Criterion: 200 idempotent no-op when player already in target team
/// </summary>
public sealed class MovePlayerHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerUserId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly ITeamRepository _teamRepository = Substitute.For<ITeamRepository>();
    private readonly IPlayerLevelReader _playerLevelReader = Substitute.For<IPlayerLevelReader>();

    private MovePlayerHandler CreateSut() =>
        new(
            _matchReader,
            _teamRepository,
            _playerLevelReader,
            NullLogger<MovePlayerHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId, string status = "Closed")
    {
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, status));
    }

    /// <summary>
    /// Builds two teams with members. Player1 is in Team A, Player2 is in Team B.
    /// </summary>
    private static (Team teamA, Team teamB, Guid player1, Guid player2) BuildTwoTeamsWithPlayers(Guid matchId)
    {
        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        var teamA = Team.Create(matchId, "Team A", FixedNow);
        var teamB = Team.Create(matchId, "Team B", FixedNow);
        teamA.AddMember(player1, FixedNow);
        teamB.AddMember(player2, FixedNow);
        return (teamA, teamB, player1, player2);
    }

    private void SetupEmptyLevelMap(IReadOnlyList<Guid> playerIds)
    {
        _playerLevelReader.GetPlayerLevelsAsync(playerIds, Arg.Any<CancellationToken>())
            .Returns(playerIds.ToDictionary(p => p, _ => "Beginner") as IReadOnlyDictionary<Guid, string>);
    }

    private void SetupLevelMapForAnyInput()
    {
        _playerLevelReader.GetPlayerLevelsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = (IReadOnlyList<Guid>)ci[0];
                return (IReadOnlyDictionary<Guid, string>)ids.ToDictionary(p => p, _ => "Beginner");
            });
    }

    // ─── 404: Match Not Found ────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 404 when match not found.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFoundException_when_match_does_not_exist()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, Guid.NewGuid(), Guid.NewGuid(), OrganizerUserId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    // ─── 403: Not Organizer ──────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 403 when caller is not the organizer.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDeniedException_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, Guid.NewGuid(), Guid.NewGuid(), OtherUserId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    // ─── 409: Wrong Match Status ─────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 409 when match status is not Closed.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidMatchStatusForTeamsException_when_match_is_not_Closed()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId, status: "Open");

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, Guid.NewGuid(), Guid.NewGuid(), OrganizerUserId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMatchStatusForTeamsException>()
            .WithMessage("*Closed*");
    }

    // ─── 404: Teams Not Yet Formed ───────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 404 when teams have not been formed yet for this match.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_TeamsNotFoundException_when_no_teams_exist()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Team>());

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, Guid.NewGuid(), Guid.NewGuid(), OrganizerUserId, CancellationToken.None);

        await act.Should().ThrowAsync<TeamsNotFoundException>();
    }

    // ─── 404: Team ID Not Found ──────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 404 when teamId is not found for this match.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_TeamNotFoundException_when_teamId_does_not_belong_to_match()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var (teamA, teamB, player1, _) = BuildTwoTeamsWithPlayers(matchId);
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new List<Team> { teamA, teamB });

        var unknownTeamId = Guid.NewGuid(); // does not belong to this match

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, unknownTeamId, player1, OrganizerUserId, CancellationToken.None);

        await act.Should().ThrowAsync<TeamNotFoundException>();
    }

    // ─── 404: Player Not In Any Team ─────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 404 when player is not a member of any team in this match.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_PlayerNotInTeamException_when_player_is_not_in_any_team()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var (teamA, teamB, _, _) = BuildTwoTeamsWithPlayers(matchId);
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new List<Team> { teamA, teamB });

        var unknownPlayerId = Guid.NewGuid(); // not in any team

        var sut = CreateSut();
        var act = async () => await sut.HandleAsync(
            matchId, teamA.Id, unknownPlayerId, OrganizerUserId, CancellationToken.None);

        await act.Should().ThrowAsync<PlayerNotInTeamException>();
    }

    // ─── 200 Idempotent No-op ────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 — 200 idempotent no-op when player is already in the target team.
    /// MovePlayerAsync must NOT be called; current state is returned.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_current_state_without_persisting_when_player_already_in_target_team()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var (teamA, teamB, player1, _) = BuildTwoTeamsWithPlayers(matchId);
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new List<Team> { teamA, teamB });

        SetupLevelMapForAnyInput();

        // player1 is already in teamA — moving to teamA is a no-op
        var sut = CreateSut();
        var result = await sut.HandleAsync(
            matchId, teamA.Id, player1, OrganizerUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Teams.Should().HaveCount(2);

        // MovePlayerAsync must NOT be called for a no-op
        await _teamRepository.DidNotReceive().MovePlayerAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ─── Successful Move ─────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 Acceptance Criterion — PUT moves player to specified team.
    /// MovePlayerAsync is called exactly once with the correct member ID and target team ID.
    /// </summary>
    [Fact]
    public async Task HandleAsync_calls_MovePlayerAsync_when_player_is_moved_to_different_team()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var (teamA, teamB, player1, _) = BuildTwoTeamsWithPlayers(matchId);
        var teams = new List<Team> { teamA, teamB };
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(teams);

        // After move, return fresh teams (reload simulation)
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(teams, teams);

        SetupLevelMapForAnyInput();

        var memberId = teamA.Members.First(m => m.PlayerId == player1).Id;

        var sut = CreateSut();
        // Move player1 from teamA to teamB
        await sut.HandleAsync(matchId, teamB.Id, player1, OrganizerUserId, CancellationToken.None);

        await _teamRepository.Received(1).MovePlayerAsync(
            memberId, teamB.Id, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.3 — handler returns updated TeamsResponse after a successful move.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_TeamsResponse_with_2_teams_after_successful_move()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, OrganizerUserId);

        var (teamA, teamB, player1, _) = BuildTwoTeamsWithPlayers(matchId);
        var teams = new List<Team> { teamA, teamB };
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(teams, teams);

        SetupLevelMapForAnyInput();

        var sut = CreateSut();
        var result = await sut.HandleAsync(
            matchId, teamB.Id, player1, OrganizerUserId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Teams.Should().HaveCount(2);
    }
}
