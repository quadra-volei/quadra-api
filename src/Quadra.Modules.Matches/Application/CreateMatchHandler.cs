using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates match creation: builds the entity, persists it, and publishes the integration event.
/// </summary>
public sealed class CreateMatchHandler
{
    private readonly IMatchRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly MatchWindowSynchronizer _windowSynchronizer;

    public CreateMatchHandler(
        IMatchRepository repository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        MatchWindowSynchronizer windowSynchronizer)
    {
        _windowSynchronizer = windowSynchronizer;
        _repository = repository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
    }

    public async Task<Match> HandleAsync(CreateMatchCommand command, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var type = Enum.Parse<Entities.MatchType>(command.Type, ignoreCase: true);
        Entities.MatchFrequency? frequency = command.Frequency is not null
            ? Enum.Parse<Entities.MatchFrequency>(command.Frequency, ignoreCase: true)
            : null;

        // "Opens N hours before" form: the window runs up to the start of the match. When that
        // moment is already past, the window is simply open from now.
        var windowOpensAt = command.ConfirmationOpensHoursBefore is { } hoursBefore
            ? command.DateTime.AddHours(-hoursBefore)
            : command.WindowOpensAt!.Value;
        var windowClosesAt = command.ConfirmationOpensHoursBefore is not null
            ? command.DateTime
            : command.WindowClosesAt!.Value;

        var match = Match.Create(
            organizerId: command.OrganizerId,
            name: command.Name,
            description: command.Description,
            address: command.Address,
            latitude: command.Latitude,
            longitude: command.Longitude,
            dateTime: command.DateTime,
            maxPlayers: command.MaxPlayers,
            regularSlots: command.RegularSlots,
            price: command.Price,
            type: type,
            frequency: frequency,
            dayOfWeekIso: command.DayOfWeek,
            windowOpensAt: windowOpensAt,
            windowClosesAt: windowClosesAt,
            now: now,
            settings: new MatchSettings(
                Format: command.Format?.ToUpperInvariant(),
                Level: command.Level is null ? null : Enum.Parse<MatchLevel>(command.Level, ignoreCase: true),
                DurationMinutes: command.DurationMinutes,
                Visibility: command.Visibility is null
                    ? MatchVisibility.Open
                    : Enum.Parse<MatchVisibility>(command.Visibility, ignoreCase: true),
                InviteMode: command.InviteMode is null
                    ? null
                    : Enum.Parse<MatchInviteMode>(command.InviteMode, ignoreCase: true),
                PriceMonthly: command.PriceMonthly,
                RecurrenceDays: command.RecurrenceDays));

        await _repository.AddAsync(match, cancellationToken);

        var @event = new MatchCreated(
            MatchId: match.Id,
            OrganizerId: match.OrganizerId,
            Name: match.Name,
            Type: match.Type.ToString(),
            DateTime: match.DateTime,
            WindowOpensAt: match.WindowOpensAt,
            WindowClosesAt: match.WindowClosesAt,
            MaxPlayers: match.MaxPlayers,
            RegularSlots: match.RegularSlots,
            DropInSlots: match.DropInSlots,
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);

        // A window that is already due opens right away, so the response shows the real status.
        return await _windowSynchronizer.SyncAsync(match, cancellationToken);
    }
}
