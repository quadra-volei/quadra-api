using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;
using Quadra.Workers.Background.Consumers;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>
/// Unit tests for <see cref="MatchSummaryGeneratedRankingConsumer"/> — the Background Worker glue that
/// decides whether a finished match awards group-ranking points. Covers the Worker side of AC-7
/// (OneOff awards nothing) and the boundary-safe composition of <see cref="FinishedMatchPoints"/> for
/// a recurring match.
/// </summary>
public sealed class MatchSummaryGeneratedRankingConsumerTests
{
    private static readonly Guid MatchId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeamA = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeamB = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PlayerA1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PlayerA2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PlayerB1 = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset MatchDateTime = new(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly IMatchGroupReader _groups = Substitute.For<IMatchGroupReader>();
    private readonly IMatchResultReader _results = Substitute.For<IMatchResultReader>();
    private readonly IMatchPointsWriter _writer = Substitute.For<IMatchPointsWriter>();

    private MatchSummaryGeneratedRankingConsumer CreateSut() =>
        new(_groups, _results, _writer, NullLogger<MatchSummaryGeneratedRankingConsumer>.Instance);

    private static MatchSummaryGenerated Event(Guid? mvp) =>
        new(
            MatchId: MatchId,
            WinnerTeamId: TeamA,
            MvpPlayerId: mvp,
            DurationSeconds: 3600,
            ParticipantPlayerIds: new[] { PlayerA1, PlayerA2, PlayerB1 },
            GeneratedAt: MatchDateTime.AddHours(2),
            OccurredAt: MatchDateTime.AddHours(2));

    private void GivenGroup(string type) =>
        _groups.GetMatchGroupAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns(new MatchGroupDescriptor(MatchId, MatchId, type, "Sunday Volleyball", MatchDateTime));

    private void GivenResult() =>
        _results.GetMatchResultAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns(new MatchResult(
                MatchId, "Ended", "BestOf3", TeamA, TeamB, 2, 1, TeamA,
                MatchDateTime, MatchDateTime.AddHours(1),
                Array.Empty<MatchResultSet>(),
                new[]
                {
                    new MatchResultTeam(TeamA, "Team A", new[] { PlayerA1, PlayerA2 }),
                    new MatchResultTeam(TeamB, "Team B", new[] { PlayerB1 }),
                },
                "Closed", PlayerA1, 3, 5));

    /// <summary>
    /// Covers AC-7 (OneOff, Worker side): when the match is OneOff the consumer awards no points —
    /// <see cref="IMatchPointsWriter"/> is never invoked, so no ledger/standing rows can be written.
    /// </summary>
    [Fact]
    public async Task Handle_oneoff_match_does_not_write_any_points()
    {
        GivenGroup("OneOff");
        var sut = CreateSut();

        await sut.HandleAsync(Event(mvp: PlayerA1), TestContext.Current.CancellationToken);

        await _writer.DidNotReceiveWithAnyArgs()
            .ApplyFinishedMatchPointsAsync(Arg.Any<FinishedMatchPoints>(), Arg.Any<CancellationToken>());
        await _results.DidNotReceiveWithAnyArgs()
            .GetMatchResultAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Supports AC-1/AC-3: for a recurring match the consumer composes a <see cref="FinishedMatchPoints"/>
    /// with the winning team's players flagged <c>Won = true</c>, losers <c>Won = false</c>, the MVP id
    /// carried through, and the grouping key = match id (MVP grouping ruling).
    /// </summary>
    [Fact]
    public async Task Handle_recurring_match_composes_and_writes_points()
    {
        GivenGroup("Recurring");
        GivenResult();
        var sut = CreateSut();

        FinishedMatchPoints? captured = null;
        await _writer.ApplyFinishedMatchPointsAsync(
            Arg.Do<FinishedMatchPoints>(p => captured = p), Arg.Any<CancellationToken>());

        await sut.HandleAsync(Event(mvp: PlayerA1), TestContext.Current.CancellationToken);

        captured.Should().NotBeNull();
        captured!.MatchId.Should().Be(MatchId);
        captured.GroupId.Should().Be(MatchId);
        captured.MvpPlayerId.Should().Be(PlayerA1);
        captured.MatchDateTime.Should().Be(MatchDateTime);
        captured.Players.Should().BeEquivalentTo(new[]
        {
            new FinishedMatchPlayerPoints(PlayerA1, Won: true),
            new FinishedMatchPlayerPoints(PlayerA2, Won: true),
            new FinishedMatchPlayerPoints(PlayerB1, Won: false),
        });
    }

    /// <summary>Supports AC-6/AC-7: an unknown match (no descriptor) writes nothing.</summary>
    [Fact]
    public async Task Handle_unknown_match_writes_nothing()
    {
        _groups.GetMatchGroupAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns((MatchGroupDescriptor?)null);
        var sut = CreateSut();

        await sut.HandleAsync(Event(mvp: null), TestContext.Current.CancellationToken);

        await _writer.DidNotReceiveWithAnyArgs()
            .ApplyFinishedMatchPointsAsync(Arg.Any<FinishedMatchPoints>(), Arg.Any<CancellationToken>());
    }
}
