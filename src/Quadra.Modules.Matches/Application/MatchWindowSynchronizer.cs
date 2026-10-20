using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Brings the status of a match in line with the clock: opens the confirmation window once
/// <see cref="Match.WindowOpensAt"/> has passed and closes it after
/// <see cref="Match.WindowClosesAt"/>. Nothing else moves a match out of Draft, so every
/// operation that depends on the window calls this first; a periodic sweep in the host does the
/// same for matches nobody is looking at.
/// </summary>
public sealed class MatchWindowSynchronizer
{
    private readonly IMatchRepository _matchRepository;
    private readonly OpenMatchWindowHandler _openHandler;
    private readonly CloseMatchWindowHandler _closeHandler;
    private readonly TimeProvider _timeProvider;

    public MatchWindowSynchronizer(
        IMatchRepository matchRepository,
        OpenMatchWindowHandler openHandler,
        CloseMatchWindowHandler closeHandler,
        TimeProvider timeProvider)
    {
        _matchRepository = matchRepository;
        _openHandler = openHandler;
        _closeHandler = closeHandler;
        _timeProvider = timeProvider;
    }

    /// <summary>Applies any due transition and returns the match in its current state.</summary>
    public async Task<Match> SyncAsync(Match match, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);

        var now = _timeProvider.GetUtcNow();

        if (match.Status == MatchStatus.Draft && now >= match.WindowOpensAt)
        {
            match = await TransitionAsync(match, _openHandler.HandleAsync, cancellationToken);
        }

        if (match.Status == MatchStatus.Open && now > match.WindowClosesAt)
        {
            match = await TransitionAsync(match, _closeHandler.HandleAsync, cancellationToken);
        }

        return match;
    }

    /// <summary>Syncs every match whose window is due. Returns how many were examined.</summary>
    public async Task<int> SyncDueAsync(CancellationToken cancellationToken)
    {
        var due = await _matchRepository.ListWindowDueAsync(_timeProvider.GetUtcNow(), cancellationToken);
        foreach (var match in due)
        {
            await SyncAsync(match, cancellationToken);
        }

        return due.Count;
    }

    private async Task<Match> TransitionAsync(
        Match match,
        Func<Guid, CancellationToken, Task> transition,
        CancellationToken cancellationToken)
    {
        try
        {
            await transition(match.Id, cancellationToken);
        }
        catch (InvalidMatchStatusTransitionException)
        {
            // A concurrent request already applied it; the reload below picks up the result.
        }

        return await _matchRepository.FindByIdAsync(match.Id, cancellationToken) ?? match;
    }
}
