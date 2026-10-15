using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchGroupReader"/> over the
/// <c>matches</c> table. The returned <see cref="MatchGroupDescriptor.GroupId"/> is the id of the
/// recurring group's first game (the match's own id for a first game or a one-off).
/// </summary>
public sealed class MatchGroupReader : IMatchGroupReader
{
    private readonly MatchesDbContext _context;

    public MatchGroupReader(MatchesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<MatchGroupDescriptor?> GetMatchGroupAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var row = await _context.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => new { m.Id, m.GroupId, m.Type, m.Name, m.DateTime })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // A recurring group is its first game's id; later games point at it (DECISIONS #64).
        return new MatchGroupDescriptor(row.Id, row.GroupId ?? row.Id, row.Type.ToString(), row.Name, row.DateTime);
    }
}
