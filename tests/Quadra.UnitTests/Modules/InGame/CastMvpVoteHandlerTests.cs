using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="CastMvpVoteHandler"/>.
///
/// Covers (F1.5 AC-1 "each player votes for ONE name, cannot self-vote"):
///   - a participant may vote for another participant (200);
///   - self-vote → 409 (SelfVoteNotAllowedException);
///   - non-participant voter → 403 (NotAParticipantException);
///   - voted player not a participant → 404 (VotedPlayerNotParticipantException).
/// Covers (F1.5 AC-3 deadline enforcement): a vote after the deadline → 409.
/// Also: 404 match / 404 voting missing; 409 when voting is not Open; DbUpdateException → 409.
/// </summary>
public sealed class CastMvpVoteHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid MatchId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Voter = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Candidate = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Outsider = new("99999999-9999-9999-9999-999999999999");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IMvpVotingRepository _votingRepository = Substitute.For<IMvpVotingRepository>();
    private readonly ITeamRepository _teamRepository = Substitute.For<ITeamRepository>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private CastMvpVoteHandler CreateSut() =>
        new(_matchReader, _votingRepository, _teamRepository, _timeProvider,
            NullLogger<CastMvpVoteHandler>.Instance);

    private void SetupMatch() =>
        _matchReader.FindMatchSummaryAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(MatchId, Guid.NewGuid(), "Closed"));

    private void SetupOpenVoting(DateTimeOffset? deadline = null)
    {
        var voting = MvpVoting.Open(MatchId, deadline ?? FixedNow.AddHours(24), FixedNow);
        _votingRepository.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(voting);
    }

    /// <summary>Builds two teams whose members are exactly the given participant ids.</summary>
    private void SetupParticipants(params Guid[] playerIds)
    {
        var teamA = Team.Create(MatchId, "Team A", FixedNow);
        var teamB = Team.Create(MatchId, "Team B", FixedNow);
        for (var i = 0; i < playerIds.Length; i++)
        {
            (i % 2 == 0 ? teamA : teamB).AddMember(playerIds[i], FixedNow);
        }
        _teamRepository.ListByMatchAsync(MatchId, Arg.Any<CancellationToken>())
            .Returns(new List<Team> { teamA, teamB });
    }

    // ─── error paths ─────────────────────────────────────────────────────────

    /// <summary>Covers: F1.5 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
    {
        _matchReader.FindMatchSummaryAsync(MatchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.5 — 404 when no voting session has been opened.</summary>
    [Fact]
    public async Task HandleAsync_throws_MvpVotingNotFound_when_voting_missing()
    {
        SetupMatch();
        _votingRepository.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<MvpVotingNotFoundException>();
    }

    /// <summary>Covers: F1.5 — 409 when the voting session is not Open (already closed).</summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_voting_closed()
    {
        SetupMatch();
        var voting = MvpVoting.Open(MatchId, FixedNow.AddHours(24), FixedNow);
        voting.Close(null, null, FixedNow);
        _votingRepository.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(voting);

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidMvpVotingStateException>().WithMessage("*closed*");
    }

    /// <summary>
    /// Covers: F1.5 AC-3 — a vote cast after the deadline is rejected with 409 (defense in depth).
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_DeadlinePassed_when_now_after_deadline()
    {
        SetupMatch();
        SetupOpenVoting(deadline: FixedNow.AddHours(-1)); // already past
        SetupParticipants(Voter, Candidate);

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<VotingDeadlinePassedException>();
    }

    /// <summary>Covers: F1.5 AC-1 — a non-participant voter is rejected with 403.</summary>
    [Fact]
    public async Task HandleAsync_throws_NotAParticipant_when_voter_not_in_teams()
    {
        SetupMatch();
        SetupOpenVoting();
        SetupParticipants(Candidate); // Voter is NOT a participant

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<NotAParticipantException>();
    }

    /// <summary>Covers: F1.5 AC-1 — a self-vote is rejected with 409.</summary>
    [Fact]
    public async Task HandleAsync_throws_SelfVote_when_voting_for_self()
    {
        SetupMatch();
        SetupOpenVoting();
        SetupParticipants(Voter, Candidate);

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Voter, CancellationToken.None);
        await act.Should().ThrowAsync<SelfVoteNotAllowedException>();
    }

    /// <summary>Covers: F1.5 AC-1 — voting for a non-participant is rejected with 404.</summary>
    [Fact]
    public async Task HandleAsync_throws_VotedPlayerNotParticipant_when_target_not_in_teams()
    {
        SetupMatch();
        SetupOpenVoting();
        SetupParticipants(Voter, Candidate);

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Outsider, CancellationToken.None);
        await act.Should().ThrowAsync<VotedPlayerNotParticipantException>();
    }

    /// <summary>Covers: F1.5 — a concurrent duplicate vote (DbUpdateException) maps to 409.</summary>
    [Fact]
    public async Task HandleAsync_maps_DbUpdateException_to_InvalidState()
    {
        SetupMatch();
        SetupOpenVoting();
        SetupParticipants(Voter, Candidate);
        _votingRepository.CastVoteAsync(
                Arg.Any<Guid>(), MatchId, Voter, Candidate, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<Task<MvpVote>>(_ => throw new DbUpdateException("duplicate"));

        var act = async () => await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidMvpVotingStateException>();
    }

    // ─── success ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.5 AC-1 — a participant casting a single vote for another participant succeeds,
    /// delegating the upsert to CastVoteAsync and returning the ballot.
    /// </summary>
    [Fact]
    public async Task HandleAsync_participant_voting_for_another_participant_succeeds()
    {
        SetupMatch();
        var voting = MvpVoting.Open(MatchId, FixedNow.AddHours(24), FixedNow);
        _votingRepository.FindByMatchAsync(MatchId, Arg.Any<CancellationToken>()).Returns(voting);
        SetupParticipants(Voter, Candidate);

        var expected = MvpVote.Create(voting.Id, MatchId, Voter, Candidate, FixedNow);
        _votingRepository.CastVoteAsync(
                voting.Id, MatchId, Voter, Candidate, FixedNow, Arg.Any<CancellationToken>())
            .Returns(expected);

        var response = await CreateSut().HandleAsync(MatchId, Voter, Candidate, CancellationToken.None);

        response.MatchId.Should().Be(MatchId);
        response.VoterPlayerId.Should().Be(Voter);
        response.VotedPlayerId.Should().Be(Candidate);
        await _votingRepository.Received(1).CastVoteAsync(
            voting.Id, MatchId, Voter, Candidate, FixedNow, Arg.Any<CancellationToken>());
    }
}
