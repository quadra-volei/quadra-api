using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="GetScoreboardHandler"/>.
///
/// Covers (F1.4):
///   - Acceptance criterion "set-by-set scoreboard": GET returns the per-set rows ordered by set number.
///   - Acceptance criterion "match state": GET reflects the current state.
///   - 404 when match not found; 404 when no scoreboard exists.
/// </summary>
public sealed class GetScoreboardHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TeamA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamB = new("22222222-2222-2222-2222-222222222222");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IScoreboardRepository _scoreboardRepository = Substitute.For<IScoreboardRepository>();

    private GetScoreboardHandler CreateSut() => new(_matchReader, _scoreboardRepository);

    private void SetupMatch(Guid matchId) =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, Guid.NewGuid(), "Closed"));

    /// <summary>Covers: F1.4 — 404 when match not found.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.4 — 404 when no scoreboard exists for the match.</summary>
    [Fact]
    public async Task HandleAsync_throws_ScoreboardNotFound_when_scoreboard_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, CancellationToken.None);
        await act.Should().ThrowAsync<ScoreboardNotFoundException>();
    }

    /// <summary>
    /// Covers: F1.4 "set-by-set scoreboard" + "match state" — GET maps the aggregate to the response
    /// with sets ordered by set number and the current state.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_scoreboard_with_ordered_sets()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId);

        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf5, TeamA, TeamB, FixedNow);
        scoreboard.Start(FixedNow);
        scoreboard.OpenSet(1, isDecidingSet: false, FixedNow);
        scoreboard.Sets.First(s => s.SetNumber == 1).Finish(TeamA, FixedNow);
        scoreboard.OpenSet(2, isDecidingSet: false, FixedNow);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var response = await CreateSut().HandleAsync(matchId, CancellationToken.None);

        response.MatchId.Should().Be(matchId);
        response.State.Should().Be("InProgress");
        response.Format.Should().Be("BestOf5");
        response.Sets.Should().HaveCount(2);
        response.Sets.Select(s => s.SetNumber).Should().ContainInOrder(1, 2);
    }
}
