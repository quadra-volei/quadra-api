using NSubstitute;
using Quadra.Api.Events;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="MatchActivityFeedConsumer"/>: which match events become an
/// activity card in the "Rede" feed.
/// </summary>
public sealed class MatchActivityFeedConsumerTests
{
    private static readonly Guid MatchId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Organizer = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Player = new("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset StartsAt = new(2026, 10, 20, 19, 30, 0, TimeSpan.Zero);

    private readonly IMatchInviteReader _invites = Substitute.For<IMatchInviteReader>();
    private readonly IActivityFeedWriter _feed = Substitute.For<IActivityFeedWriter>();

    private MatchActivityFeedConsumer CreateSut() => new(_invites, _feed);

    private void GivenInvitableFor(Guid playerId) =>
        _invites.FindInvitableAsync(MatchId, playerId, Arg.Any<CancellationToken>())
            .Returns(new MatchInvite(MatchId, "Racha da Galera", StartsAt, Organizer));

    private static MatchCreated Created() =>
        new(MatchId, Organizer, "Racha da Galera", "OneOff", StartsAt, StartsAt.AddDays(-1), StartsAt, 12, 12, 0, StartsAt.AddDays(-2));

    private static PresenceConfirmed Confirmed(Guid playerId) =>
        new(MatchId, playerId, "DropIn", StartsAt.AddDays(-1), StartsAt.AddDays(-1));

    [Fact]
    public async Task Creating_an_open_match_adds_the_created_card_for_the_organizer()
    {
        GivenInvitableFor(Organizer);

        await CreateSut().HandleAsync(Created(), TestContext.Current.CancellationToken);

        await _feed.Received(1).AddMatchActivityAsync(
            Organizer, MatchActivity.Created, MatchId, "Racha da Galera", StartsAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_player_confirming_in_an_open_match_adds_the_confirmed_card()
    {
        GivenInvitableFor(Player);

        await CreateSut().HandleAsync(Confirmed(Player), TestContext.Current.CancellationToken);

        await _feed.Received(1).AddMatchActivityAsync(
            Player, MatchActivity.Confirmed, MatchId, "Racha da Galera", StartsAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_organizer_confirming_in_their_own_match_adds_nothing()
    {
        GivenInvitableFor(Organizer);

        await CreateSut().HandleAsync(Confirmed(Organizer), TestContext.Current.CancellationToken);

        await _feed.DidNotReceiveWithAnyArgs().AddMatchActivityAsync(default, default, default, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_match_that_cannot_be_invited_to_never_reaches_the_feed()
    {
        // The reader answers null for a private, started, cancelled or missing match.
        await CreateSut().HandleAsync(Created(), TestContext.Current.CancellationToken);
        await CreateSut().HandleAsync(Confirmed(Player), TestContext.Current.CancellationToken);

        await _feed.DidNotReceiveWithAnyArgs().AddMatchActivityAsync(default, default, default, default!, default, TestContext.Current.CancellationToken);
    }
}
