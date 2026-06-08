using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Loads a single match by its identifier, throwing <see cref="MatchNotFoundException"/> if absent.
/// </summary>
public sealed class GetMatchHandler
{
    private readonly IMatchRepository _repository;

    public GetMatchHandler(IMatchRepository repository)
    {
        _repository = repository;
    }

    public async Task<Match> HandleAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await _repository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);
    }
}
