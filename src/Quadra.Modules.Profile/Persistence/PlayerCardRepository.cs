using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>EF Core implementation of <see cref="IPlayerCardRepository"/>.</summary>
public sealed class PlayerCardRepository : IPlayerCardRepository
{
    private readonly ProfileDbContext _context;

    public PlayerCardRepository(ProfileDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<PlayerCardLookup> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var card = await _context.PlayerCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (card is not null)
        {
            return new PlayerCardLookup(card, ProfileExists: true, card.MatchesPlayed);
        }

        var profileExists = await _context.PlayerProfiles
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId, cancellationToken);

        if (!profileExists)
        {
            return new PlayerCardLookup(Card: null, ProfileExists: false, MatchesPlayed: 0);
        }

        var matchesPlayed = await _context.PlayerStats
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => (int?)s.MatchesPlayed)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        return new PlayerCardLookup(Card: null, ProfileExists: true, matchesPlayed);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(PlayerCard card, CancellationToken cancellationToken)
    {
        var exists = await _context.PlayerCards
            .AsNoTracking()
            .AnyAsync(c => c.UserId == card.UserId, cancellationToken);

        if (exists)
        {
            _context.PlayerCards.Update(card);
        }
        else
        {
            await _context.PlayerCards.AddAsync(card, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
