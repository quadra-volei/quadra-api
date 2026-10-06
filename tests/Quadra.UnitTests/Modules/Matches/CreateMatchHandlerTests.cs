using FluentAssertions;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="CreateMatchHandler"/>.
/// Covers AC-8: MatchCreated event is published after repository commit.
/// </summary>
public sealed class CreateMatchHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IMatchRepository _repository = Substitute.For<IMatchRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private CreateMatchHandler CreateSut() =>
        new(_repository, _eventPublisher, _timeProvider, MatchTestSupport.Synchronizer(_repository, _timeProvider));

    private static CreateMatchCommand BuildValidOneOffCommand(Guid? organizerId = null) =>
        new(
            OrganizerId: organizerId ?? Guid.NewGuid(),
            Name: "Sunday Volleyball",
            Description: "Come play!",
            Address: "Rua Teste, 123",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: FixedNow.AddDays(7),
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "OneOff",
            Frequency: null,
            DayOfWeek: null,
            WindowOpensAt: FixedNow.AddDays(1),
            WindowClosesAt: FixedNow.AddDays(6));

    private static CreateMatchCommand BuildValidRecurringCommand(Guid? organizerId = null) =>
        new(
            OrganizerId: organizerId ?? Guid.NewGuid(),
            Name: "Weekly Volleyball",
            Description: null,
            Address: "Rua Teste, 123",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: FixedNow.AddDays(7),
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "Recurring",
            Frequency: "Weekly",
            DayOfWeek: 3,
            WindowOpensAt: FixedNow.AddDays(1),
            WindowClosesAt: FixedNow.AddDays(6));

    // ─── Normal flow ─────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-1 — handler returns a Match with Status=Draft for a valid OneOff command.
    /// </summary>
    [Fact]
    public async Task HandleAsync_OneOff_command_returns_Match_with_Draft_status()
    {
        var command = BuildValidOneOffCommand();
        var sut = CreateSut();

        var result = await sut.HandleAsync(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be(MatchStatus.Draft);
        result.Type.Should().Be(MatchType.OneOff);
        result.Name.Should().Be(command.Name);
        result.OrganizerId.Should().Be(command.OrganizerId);
    }

    /// <summary>
    /// Covers: AC-2 — handler returns a Match correctly populated for a Recurring command.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Recurring_command_returns_Match_with_correct_frequency_and_day()
    {
        var command = BuildValidRecurringCommand();
        var sut = CreateSut();

        var result = await sut.HandleAsync(command, CancellationToken.None);

        result.Type.Should().Be(MatchType.Recurring);
        result.Frequency.Should().Be(MatchFrequency.Weekly);
        result.DayOfWeek.Should().Be(3);
    }

    /// <summary>
    /// Covers: AC-8 — MatchCreated event is published after AddAsync (repository commit).
    /// Verifies ordering: repository.AddAsync must be called before publisher.PublishAsync.
    /// </summary>
    [Fact]
    public async Task HandleAsync_publishes_MatchCreated_event_AFTER_repository_AddAsync()
    {
        var command = BuildValidOneOffCommand();
        var sut = CreateSut();

        var addCalled = false;
        var publishCalledBeforeAdd = false;

        _repository.AddAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                addCalled = true;
                return Task.CompletedTask;
            });

        _eventPublisher.PublishAsync(Arg.Any<MatchCreated>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (!addCalled)
                {
                    publishCalledBeforeAdd = true;
                }

                return Task.CompletedTask;
            });

        await sut.HandleAsync(command, CancellationToken.None);

        publishCalledBeforeAdd.Should().BeFalse("MatchCreated must only be published AFTER the repository add succeeds");
        await _eventPublisher.Received(1).PublishAsync(Arg.Any<MatchCreated>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: AC-8 — MatchCreated event contains the correct fields from the created match.
    /// </summary>
    [Fact]
    public async Task HandleAsync_publishes_MatchCreated_with_correct_event_fields()
    {
        var organizerId = Guid.NewGuid();
        var command = BuildValidOneOffCommand(organizerId);
        var sut = CreateSut();

        Match? savedMatch = null;
        _repository.AddAsync(Arg.Do<Match>(m => savedMatch = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await sut.HandleAsync(command, CancellationToken.None);

        savedMatch.Should().NotBeNull();

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchCreated>(e =>
                e.MatchId == savedMatch!.Id
                && e.OrganizerId == organizerId
                && e.Name == command.Name
                && e.Type == "OneOff"
                && e.OccurredAt == FixedNow),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: handler calls repository.AddAsync exactly once.
    /// </summary>
    [Fact]
    public async Task HandleAsync_calls_repository_AddAsync_exactly_once()
    {
        var command = BuildValidOneOffCommand();
        var sut = CreateSut();

        await sut.HandleAsync(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
