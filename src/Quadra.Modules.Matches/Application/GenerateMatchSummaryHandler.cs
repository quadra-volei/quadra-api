using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;

// The entity MatchSummary collides by simple name with the Quadra.Shared.Contracts.MatchSummary
// projection record imported above. Alias fixes the reference unambiguously to the Matches entity.
using MatchSummary = Quadra.Modules.Matches.Entities.MatchSummary;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Finalizes an ended match into a single immutable summary: authorize the organizer, snapshot the
/// InGame result, transition <c>matches.status</c> to <c>Ended</c>, persist the summary and publish
/// the completion events. The record cannot be regenerated (immutable).
/// </summary>
public sealed class GenerateMatchSummaryHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IMatchSummaryRepository _summaryRepository;
    private readonly IMatchResultReader _matchResultReader;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GenerateMatchSummaryHandler> _logger;

    public GenerateMatchSummaryHandler(
        IMatchRepository matchRepository,
        IMatchSummaryRepository summaryRepository,
        IMatchResultReader matchResultReader,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<GenerateMatchSummaryHandler> logger)
    {
        _matchRepository = matchRepository;
        _summaryRepository = summaryRepository;
        _matchResultReader = matchResultReader;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MatchSummaryResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        // 1. Load the match.
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Only the organizer may finalize the summary.
        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Immutability guard: a summary can be generated at most once.
        if (await _summaryRepository.ExistsForMatchAsync(matchId, cancellationToken))
        {
            throw new MatchSummaryAlreadyExistsException(matchId);
        }

        // 4. Read the InGame result to snapshot.
        var result = await _matchResultReader.GetMatchResultAsync(matchId, cancellationToken)
            ?? throw new ScoreboardNotFoundForSummaryException(
                "No scoreboard has been created for this match.");

        // 5. The game must have ended.
        if (result.ScoreboardState != "Ended")
        {
            throw new MatchNotEndedException(
                "The match summary can only be generated after the game has ended.");
        }

        // 6. MVP voting, if any, must be closed.
        if (result.MvpVotingState == "Open")
        {
            throw new MvpVotingStillOpenException(
                "Close MVP voting before generating the match summary.");
        }

        // 7. A cancelled match cannot be summarized.
        if (match.Status is MatchStatus.Cancelled)
        {
            throw new MatchNotEndedException("A cancelled match cannot be summarized.");
        }

        var now = _timeProvider.GetUtcNow();

        // 8. Build the immutable snapshot aggregate (duration is computed inside Generate).
        var summary = MatchSummary.Generate(matchId, result, generatedBy: callerId, now);

        // 9. Transition matches.status -> Ended (no-op when already Ended).
        var previousStatus = match.Status;
        var statusChanged = match.MarkEnded(now);

        // 10. Persist the summary (its UNIQUE (match_id) is a last-resort race guard) and the
        //     (possibly) mutated match.
        try
        {
            await _summaryRepository.AddAsync(summary, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new MatchSummaryAlreadyExistsException(matchId);
        }

        if (statusChanged)
        {
            await _matchRepository.UpdateAsync(match, cancellationToken);
        }

        _logger.LogInformation(
            "Match summary generated. MatchId={MatchId} WinnerTeamId={WinnerTeamId} "
            + "MvpPlayerId={MvpPlayerId} DurationSeconds={DurationSeconds}",
            matchId, summary.WinnerTeamId, summary.MvpPlayerId, summary.DurationSeconds);

        // 11. Publish integration events after commit.
        var participantPlayerIds = summary.Players
            .Select(p => p.PlayerId)
            .OrderBy(id => id)
            .ToList();

        await _eventPublisher.PublishAsync(
            new MatchSummaryGenerated(
                MatchId: matchId,
                WinnerTeamId: summary.WinnerTeamId ?? Guid.Empty,
                MvpPlayerId: summary.MvpPlayerId,
                DurationSeconds: summary.DurationSeconds,
                ParticipantPlayerIds: participantPlayerIds,
                GeneratedAt: summary.GeneratedAt,
                OccurredAt: now),
            cancellationToken);

        if (statusChanged)
        {
            await _eventPublisher.PublishAsync(
                new MatchStatusChanged(
                    MatchId: matchId,
                    OrganizerId: match.OrganizerId,
                    PreviousStatus: previousStatus.ToString(),
                    NewStatus: match.Status.ToString(),
                    OccurredAt: now),
                cancellationToken);
        }

        // 12. Map and return.
        return GetMatchSummaryHandler.MapToResponse(summary);
    }
}
