using Microsoft.EntityFrameworkCore;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITeamRepository"/>.
/// </summary>
public sealed class TeamRepository : ITeamRepository
{
    private readonly InGameDbContext _context;

    public TeamRepository(InGameDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task ReplaceTeamsAsync(
        Guid matchId,
        IReadOnlyList<Team> teams,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Delete existing members then teams.
        var existingTeamIds = await _context.Teams
            .Where(t => t.MatchId == matchId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (existingTeamIds.Count > 0)
        {
            await _context.TeamMembers
                .Where(m => existingTeamIds.Contains(m.TeamId))
                .ExecuteDeleteAsync(cancellationToken);

            await _context.Teams
                .Where(t => t.MatchId == matchId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Insert new teams (without Members — not mapped as EF navigation).
        await _context.Teams.AddRangeAsync(teams, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Insert members separately.
        var allMembers = teams.SelectMany(t => t.Members).ToList();
        await _context.TeamMembers.AddRangeAsync(allMembers, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Team>> ListByMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var teams = await _context.Teams
            .AsNoTracking()
            .Where(t => t.MatchId == matchId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        if (teams.Count == 0)
        {
            return teams;
        }

        var teamIds = teams.Select(t => t.Id).ToList();
        var members = await _context.TeamMembers
            .AsNoTracking()
            .Where(m => teamIds.Contains(m.TeamId))
            .ToListAsync(cancellationToken);

        // Attach members to their respective teams.
        var membersByTeam = members.ToLookup(m => m.TeamId);
        foreach (var team in teams)
        {
            team.AttachMembers(membersByTeam[team.Id]);
        }

        return teams;
    }

    /// <inheritdoc/>
    public async Task MovePlayerAsync(
        Guid memberId,
        Guid targetTeamId,
        CancellationToken cancellationToken)
    {
        var member = await _context.TeamMembers
            .FirstAsync(m => m.Id == memberId, cancellationToken);

        member.MoveTo(targetTeamId);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
