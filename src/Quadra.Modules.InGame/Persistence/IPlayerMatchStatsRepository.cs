using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

public interface IPlayerMatchStatsRepository
{
    Task<IReadOnlyList<PlayerMatchStats>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>Makes the match's rows exactly <paramref name="rows"/> (upsert, and delete the rest).</summary>
    Task ReplaceAsync(Guid matchId, IReadOnlyList<PlayerMatchStats> rows, CancellationToken cancellationToken);
}
