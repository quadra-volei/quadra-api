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
/// Unit tests for <see cref="CreateScoreboardHandler"/>.
///
/// Covers (F1.4):
///   - Acceptance criterion "match state" — a created scoreboard is in NotStarted state.
///   - 404 when the match does not exist.
///   - 403 when the caller is not the organizer.
///   - 409 when the match status is not Closed.
///   - 404 when teams have not been formed (&lt; 2 teams).
///   - 409 when a scoreboard already exists.
///   - Deterministic team A/B assignment (teams ordered by name ascending).
/// </summary>
public sealed class CreateScoreboardHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly ITeamRepository _teamRepository = Substitute.For<ITeamRepository>();
    private readonly IScoreboardRepository _scoreboardRepository = Substitute.For<IScoreboardRepository>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private CreateScoreboardHandler CreateSut() =>
        new(_matchReader, _teamRepository, _scoreboardRepository, _timeProvider,
            NullLogger<CreateScoreboardHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId, string status = "Closed") =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, status));

    private void SetupTeams(Guid matchId, params Team[] teams) =>
        _teamRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(teams);

    // ─── 404: match not found ────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when match not found.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_does_not_exist()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf3, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    // ─── 403: not organizer ──────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDenied_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);

        var act = async () => await CreateSut().HandleAsync(
            matchId, Other, ScoreboardFormat.BestOf3, CancellationToken.None);

        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    // ─── 409: match not Closed ───────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 409 before confirmations open or once the game is under way.</summary>
    [Theory]
    [InlineData("Draft")]
    [InlineData("InProgress")]
    public async Task HandleAsync_throws_InvalidState_when_match_is_not_Closed(string status)
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer, status);

        var act = async () => await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf3, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidScoreboardStateException>()
            .WithMessage("*teams are formed*");
    }

    // ─── 404: teams not formed ───────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when fewer than two teams have been formed.</summary>
    [Fact]
    public async Task HandleAsync_throws_TeamsNotFormed_when_fewer_than_two_teams()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupTeams(matchId, Team.Create(matchId, "Team A", FixedNow));

        var act = async () => await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf3, CancellationToken.None);

        await act.Should().ThrowAsync<TeamsNotFormedException>();
    }

    // ─── 409: scoreboard already exists ──────────────────────────────────────

    /// <summary>Covers: F1.4 — 409 when a scoreboard already exists for the match.</summary>
    [Fact]
    public async Task HandleAsync_throws_AlreadyExists_when_scoreboard_present()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupTeams(matchId,
            Team.Create(matchId, "Team A", FixedNow),
            Team.Create(matchId, "Team B", FixedNow));

        var existing = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, Guid.NewGuid(), Guid.NewGuid(), FixedNow);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(existing);

        var act = async () => await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf3, CancellationToken.None);

        await act.Should().ThrowAsync<ScoreboardAlreadyExistsException>();
    }

    // ─── success: NotStarted + deterministic team assignment ─────────────────

    /// <summary>
    /// Covers: F1.4 "match state" — a fresh scoreboard is created in NotStarted state with no sets,
    /// current set 0, and is persisted once.
    /// </summary>
    [Fact]
    public async Task HandleAsync_creates_scoreboard_in_NotStarted_state_and_persists()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupTeams(matchId,
            Team.Create(matchId, "Team A", FixedNow),
            Team.Create(matchId, "Team B", FixedNow));
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var response = await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf3, CancellationToken.None);

        response.State.Should().Be("NotStarted");
        response.Format.Should().Be("BestOf3");
        response.CurrentSetNumber.Should().Be(0);
        response.Sets.Should().BeEmpty();
        response.WinnerTeamId.Should().BeNull();

        await _scoreboardRepository.Received(1).AddAsync(
            Arg.Is<Scoreboard>(s => s.MatchId == matchId && s.State == ScoreboardState.NotStarted),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.4 implementation note — deterministic team A/B assignment: with the repository
    /// returning teams ordered by name ascending, teamAId is the first ("Team A") and teamBId the second.
    /// </summary>
    [Fact]
    public async Task HandleAsync_assigns_teamA_and_teamB_by_repository_order()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var teamA = Team.Create(matchId, "Team A", FixedNow);
        var teamB = Team.Create(matchId, "Team B", FixedNow);
        SetupTeams(matchId, teamA, teamB); // repository guarantees name-ascending order
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var response = await CreateSut().HandleAsync(
            matchId, Organizer, ScoreboardFormat.BestOf5, CancellationToken.None);

        response.TeamAId.Should().Be(teamA.Id);
        response.TeamBId.Should().Be(teamB.Id);
    }
}
