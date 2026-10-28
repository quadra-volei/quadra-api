using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP tests for a post that invites to a match still to come (<c>POST /api/v1/posts</c> with
/// the id of a match that is not in the author's history), against a real Postgres: the
/// organizer and a confirmed player may invite to an open match, nobody else, and never to a
/// private one.
/// </summary>
public sealed class PostInviteEndpointsTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;

    public PostInviteEndpointsTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> CreateMatchAsync(Guid organizerId, DateTimeOffset startsAt, string visibility = "Open")
    {
        var response = await _fx.CreateAuthenticatedClient(organizerId).PostAsJsonAsync(
            "/api/v1/matches",
            new Dictionary<string, object?>
            {
                ["name"] = "Racha da Galera",
                ["address"] = "Arena Central, São Paulo",
                ["latitude"] = -23.5505,
                ["longitude"] = -46.6333,
                ["dateTime"] = startsAt,
                ["maxPlayers"] = 4,
                ["regularSlots"] = 4,
                ["price"] = 0m,
                ["type"] = "OneOff",
                ["format"] = "4X4",
                ["level"] = "Intermediate",
                ["durationMinutes"] = 90,
                ["visibility"] = visibility,
                ["inviteMode"] = visibility == "Private" ? "Code" : null,
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostAsync(Guid authorId, Guid matchId) =>
        _fx.CreateAuthenticatedClient(authorId)
            .PostAsJsonAsync("/api/v1/posts", new { text = "Bora jogar?", matchId }, Ct);

    [Fact]
    public async Task The_organizer_and_a_confirmed_player_can_invite_to_an_open_match()
    {
        var organizer = Guid.NewGuid();
        var player = Guid.NewGuid();
        var startsAt = DateTimeOffset.UtcNow.AddHours(3);
        var matchId = await CreateMatchAsync(organizer, startsAt);
        (await _fx.CreateAuthenticatedClient(player).PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();

        var created = await PostAsync(organizer, matchId);
        (await PostAsync(player, matchId)).StatusCode.Should().Be(HttpStatusCode.Created);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var match = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("match");
        match.GetProperty("id").GetGuid().Should().Be(matchId);
        match.GetProperty("name").GetString().Should().Be("Racha da Galera");
        match.GetProperty("startsAt").GetDateTimeOffset().Should().BeCloseTo(startsAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Only_the_organizer_shares_the_match_itself_and_only_once()
    {
        var organizer = Guid.NewGuid();
        var player = Guid.NewGuid();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddHours(3));
        await _fx.CreateAuthenticatedClient(player).PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct);
        var organizerClient = _fx.CreateAuthenticatedClient(organizer);

        var first = await organizerClient.PostAsJsonAsync("/api/v1/posts", new { matchId }, Ct);
        var second = await organizerClient.PostAsJsonAsync("/api/v1/posts", new { matchId }, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        card.GetProperty("kind").GetString().Should().Be("MatchCreated");
        card.GetProperty("text").ValueKind.Should().Be(JsonValueKind.Null);
        card.GetProperty("match").GetProperty("name").GetString().Should().Be("Racha da Galera");
        // Sharing again posts nothing: 409, so the app says it is already on the feed (DECISIONS #100).
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        // A confirmed player may invite with their own words, not share the match itself.
        (await _fx.CreateAuthenticatedClient(player).PostAsJsonAsync("/api/v1/posts", new { matchId }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Someone_outside_the_match_cannot_invite_to_it()
    {
        var matchId = await CreateMatchAsync(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(3));

        (await PostAsync(Guid.NewGuid(), matchId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Nobody_invites_to_a_private_match()
    {
        var organizer = Guid.NewGuid();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddHours(3), visibility: "Private");

        (await PostAsync(organizer, matchId)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
