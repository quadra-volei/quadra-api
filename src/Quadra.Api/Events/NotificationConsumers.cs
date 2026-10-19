using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Events.Matches;

namespace Quadra.Api.Events;

/// <summary>
/// Turns game events into the essential notifications (DECISIONS #77): a spot from the waiting
/// list, the MVP vote open, the summary ready. Each goes once per player (dedup key).
/// </summary>
public sealed class NotificationConsumers :
    IEventHandler<WaitingListPromoted>,
    IEventHandler<MvpVotingOpened>,
    IEventHandler<MatchSummaryGenerated>
{
    private readonly INotificationSender _sender;
    private readonly IMatchDescriptorReader _matches;
    private readonly IMatchReader _matchReader;
    private readonly ITeamRepository _teams;

    public NotificationConsumers(
        INotificationSender sender,
        IMatchDescriptorReader matches,
        IMatchReader matchReader,
        ITeamRepository teams)
    {
        _sender = sender;
        _matches = matches;
        _matchReader = matchReader;
        _teams = teams;
    }

    public async Task HandleAsync(WaitingListPromoted @event, CancellationToken cancellationToken)
    {
        var name = await MatchNameAsync(@event.MatchId, cancellationToken);
        await _sender.SendAsync(
            [@event.PlayerId],
            new NotificationRequest(
                "WaitingListSpot",
                NotificationCategory.Reminders,
                "Abriu uma vaga pra você",
                $"Você saiu da lista de espera de {name}. Confirme sua presença.",
                @event.MatchId,
                $"spot:{@event.MatchId}:{@event.OccurredAt:O}"),
            cancellationToken);
    }

    public async Task HandleAsync(MvpVotingOpened @event, CancellationToken cancellationToken)
    {
        // Who played (accounts only, not guests) and the organizer, who also votes.
        var teams = await _teams.ListByMatchAsync(@event.MatchId, cancellationToken);
        var voters = teams.SelectMany(t => t.PlayerIds).ToList();
        if (await _matchReader.FindMatchSummaryAsync(@event.MatchId, cancellationToken) is { } match)
        {
            voters.Add(match.OrganizerId);
        }

        var name = await MatchNameAsync(@event.MatchId, cancellationToken);
        await _sender.SendAsync(
            voters,
            new NotificationRequest(
                "MvpVotingOpen",
                NotificationCategory.Ranking,
                "Quem foi o MVP?",
                $"A votação de {name} está aberta por 24 horas.",
                @event.MatchId,
                $"mvp:{@event.MatchId}"),
            cancellationToken);
    }

    public async Task HandleAsync(MatchSummaryGenerated @event, CancellationToken cancellationToken)
    {
        var name = await MatchNameAsync(@event.MatchId, cancellationToken);
        await _sender.SendAsync(
            @event.ParticipantPlayerIds,
            new NotificationRequest(
                "SummaryReady",
                NotificationCategory.Ranking,
                "Resumo do jogo pronto",
                $"Veja placar, MVP e seus pontos em {name}.",
                @event.MatchId,
                $"summary:{@event.MatchId}"),
            cancellationToken);
    }

    private async Task<string> MatchNameAsync(Guid matchId, CancellationToken cancellationToken) =>
        (await _matches.GetMatchDescriptorAsync(matchId, cancellationToken))?.Name ?? "sua partida";
}
