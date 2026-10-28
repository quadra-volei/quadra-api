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
    private readonly MatchArenaResolver _arenaResolver;

    public CreateMatchHandler(
        IMatchRepository repository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        MatchWindowSynchronizer windowSynchronizer,
        MatchArenaResolver arenaResolver)
    {
        _windowSynchronizer = windowSynchronizer;
        _arenaResolver = arenaResolver;
        _repository = repository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
    }

    public async Task<Match> HandleAsync(CreateMatchCommand command, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var arenaId = await _arenaResolver.ResolveAsync(command.ArenaId, command.PlaceId, cancellationToken);

        var type = Enum.Parse<Entities.MatchType>(command.Type, ignoreCase: true);
        Entities.MatchFrequency? frequency = command.Frequency is not null
            ? Enum.Parse<Entities.MatchFrequency>(command.Frequency, ignoreCase: true)
            : null;

        // "Opens N hours before" form: the window runs up to the start of the match. When that
        // moment is already past, the window is simply open from now. With no window sent at
        // all (the mobile form), confirmations are open from creation until the start.
        var windowOpensAt = command.ConfirmationOpensHoursBefore is { } hoursBefore
            ? command.DateTime.AddHours(-hoursBefore)
            : command.WindowOpensAt ?? now;
        var windowClosesAt = command.ConfirmationOpensHoursBefore is not null
            ? command.DateTime
            : command.WindowClosesAt ?? command.DateTime;

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
                GamePoints: command.GamePoints,
                FreeTeams: command.FreeTeams ?? false,
                Visibility: command.Visibility is null
                    ? MatchVisibility.Open
                    : Enum.Parse<MatchVisibility>(command.Visibility, ignoreCase: true),
                // Every private match is joined by its invite code (link): "Somente convidados"
                // left the organizer no way to bring a player in, so the invite never worked.
                InviteMode: MatchInviteMode.Code,
                PriceMonthly: command.PriceMonthly,
                RecurrenceDays: command.RecurrenceDays,
                CoverObjectKey: command.CoverObjectKey,
                Gender: command.Gender is null ? null : Enum.Parse<MatchGender>(command.Gender, ignoreCase: true),
                Surface: command.Surface is null ? null : Enum.Parse<MatchSurface>(command.Surface, ignoreCase: true)));

        if (arenaId is not null)
        {
            match.LinkArena(arenaId, now);
        }

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
