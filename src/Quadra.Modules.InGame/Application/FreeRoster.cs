using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// The one roster of a match without fixed teams (<see cref="ScoreboardFormat.Free"/>): every
/// confirmed player with an account. It is what makes MVP voting, the summary, history and
/// stats see who played, with no team split shown anywhere.
/// </summary>
internal static class FreeRoster
{
    public const string Name = "Todos";

    public static async Task<Team> ReplaceAsync(
        Guid matchId,
        IConfirmedPlayersReader confirmedPlayers,
        ITeamRepository teams,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var roster = Team.Create(matchId, Name, now);
        foreach (var playerId in await confirmedPlayers.GetConfirmedPlayerIdsAsync(matchId, cancellationToken))
        {
            roster.AddMember(playerId, now);
        }

        await teams.ReplaceTeamsAsync(matchId, [roster], cancellationToken);
        return roster;
    }
}
