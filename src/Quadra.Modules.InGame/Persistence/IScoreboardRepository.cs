using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Persistence contract for the <see cref="Scoreboard"/> aggregate (scoreboard + its sets).
/// </summary>
public interface IScoreboardRepository
{
    /// <summary>
    /// Inserts a new <paramref name="scoreboard"/> (and any sets it already holds) in a single save.
    /// </summary>
    Task AddAsync(Scoreboard scoreboard, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the scoreboard for <paramref name="matchId"/> with its sets ordered by set number,
    /// or <c>null</c> if no scoreboard exists for the match. Entities are change-tracked so callers
    /// can mutate them and persist via <see cref="UpdateAsync"/>.
    /// </summary>
    Task<Scoreboard?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes to <paramref name="scoreboard"/> and its sets (including
    /// newly opened sets) inside a single database transaction.
    /// </summary>
    Task UpdateAsync(Scoreboard scoreboard, CancellationToken cancellationToken);
}
