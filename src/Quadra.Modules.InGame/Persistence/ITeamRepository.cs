using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Persistence contract for <see cref="Team"/> aggregate operations.
/// </summary>
public interface ITeamRepository
{
    /// <summary>
    /// Deletes all existing teams and members for <paramref name="matchId"/>, then inserts
    /// <paramref name="teams"/> in a single database transaction (idempotent re-draft).
    /// </summary>
    Task ReplaceTeamsAsync(Guid matchId, IReadOnlyList<Team> teams, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all teams (with their members) for the given <paramref name="matchId"/>.
    /// Returns an empty list if no teams have been drafted yet.
    /// </summary>
    Task<IReadOnlyList<Team>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the <c>team_id</c> on the <see cref="TeamMember"/> row identified by
    /// <paramref name="memberId"/> to <paramref name="targetTeamId"/>.
    /// </summary>
    Task MovePlayerAsync(Guid memberId, Guid targetTeamId, CancellationToken cancellationToken);
}
