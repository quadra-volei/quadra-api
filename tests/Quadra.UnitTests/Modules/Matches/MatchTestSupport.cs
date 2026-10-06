using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>Shared builders for the Matches unit tests.</summary>
internal static class MatchTestSupport
{
    /// <summary>
    /// A real <see cref="MatchWindowSynchronizer"/> over the given (substituted) repositories, so
    /// handler tests exercise the actual open/close rules.
    /// </summary>
    public static MatchWindowSynchronizer Synchronizer(
        IMatchRepository matchRepository,
        TimeProvider timeProvider,
        IPresenceRepository? presenceRepository = null,
        IWaitingListRepository? waitingListRepository = null,
        IEventPublisher? eventPublisher = null)
    {
        presenceRepository ??= Substitute.For<IPresenceRepository>();
        waitingListRepository ??= Substitute.For<IWaitingListRepository>();
        eventPublisher ??= Substitute.For<IEventPublisher>();

        return new MatchWindowSynchronizer(
            matchRepository,
            new OpenMatchWindowHandler(
                matchRepository,
                presenceRepository,
                eventPublisher,
                timeProvider,
                NullLogger<OpenMatchWindowHandler>.Instance),
            new CloseMatchWindowHandler(
                matchRepository,
                presenceRepository,
                waitingListRepository,
                eventPublisher,
                timeProvider,
                NullLogger<CloseMatchWindowHandler>.Instance),
            timeProvider);
    }

    /// <summary>
    /// A match starting in a week whose window is <paramref name="windowOpensAt"/> →
    /// <paramref name="windowClosesAt"/>, forced into <paramref name="status"/>.
    /// </summary>
    public static Match Match(
        Guid organizerId,
        DateTimeOffset now,
        DateTimeOffset windowOpensAt,
        DateTimeOffset windowClosesAt,
        MatchStatus status,
        int maxPlayers = 4,
        int regularSlots = 4,
        MatchSettings? settings = null)
    {
        var match = Quadra.Modules.Matches.Entities.Match.Create(
            organizerId: organizerId,
            name: "Racha de quinta",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: now.AddDays(7),
            maxPlayers: maxPlayers,
            regularSlots: regularSlots,
            price: null,
            type: Quadra.Modules.Matches.Entities.MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: windowOpensAt,
            windowClosesAt: windowClosesAt,
            now: now,
            settings: settings);

        typeof(Match).GetProperty(nameof(Quadra.Modules.Matches.Entities.Match.Status))!.SetValue(match, status);
        return match;
    }
}
