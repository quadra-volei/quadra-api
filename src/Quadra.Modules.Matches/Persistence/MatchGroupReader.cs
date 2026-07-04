using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchGroupReader"/> over the
/// <c>matches</c> table. In the MVP the recurring <c>matches</c> row <i>is</i> the group, so the
/// returned <see cref="MatchGroupDescriptor.GroupId"/> equals the match id (SCOPE ruling 2026-07-03).
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
            .Select(m => new { m.Id, m.Type, m.Name, m.DateTime })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // GroupId == MatchId in the MVP; the column is kept distinct so a future recurring-occurrence
        // model can repoint it with no schema change.
        return new MatchGroupDescriptor(row.Id, row.Id, row.Type.ToString(), row.Name, row.DateTime);
    }
}
