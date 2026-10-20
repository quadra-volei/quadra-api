using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Concurrent requests on one match (audit SEC-03, SEC-11): confirmations never overfill the
/// slots, simultaneous waiting-list joins all get distinct positions, no request answers 500.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class ConcurrencyAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public ConcurrencyAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Capacity: concurrent confirmations must never overfill the regular slots.
    [Fact]
    public async Task Concurrent_confirmations_never_exceed_regular_slots()
    {
        var organizer = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(2), regularSlots: 2, recurring: false, opensHoursBefore: 72);
        var players = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _fx.LoginAsync()));

        var responses = await Task.WhenAll(players.Select(p =>
            Safe(p.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))));

        var presences = await Read(await organizer.Client.GetAsync($"/api/v1/matches/{matchId}/presences", Ct));
        var confirmed = presences.GetProperty("confirmed").GetArrayLength();
        var waiting = presences.GetProperty("waitingList").EnumerateArray().Select(w => w.GetProperty("position").GetInt32()).ToList();

        using var _ = new AssertionScope();
        confirmed.Should().BeLessThanOrEqualTo(2, $"12 players confirmed at once on a 2-slot match; codes: {Codes(responses)}");
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError, $"codes: {Codes(responses)}");
        waiting.Should().OnlyHaveUniqueItems();
    }

    // Waiting list: simultaneous joins on a full match get distinct positions, never a server error.
    [Fact]
    public async Task Concurrent_joins_on_a_full_match_queue_cleanly()
    {
        var organizer = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(2), regularSlots: 1, recurring: false, opensHoursBefore: 72);
        await Ok(organizer.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct));
        var players = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _fx.LoginAsync()));

        var responses = await Task.WhenAll(players.Select(p =>
            Safe(p.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))));
        var presences = await Read(await organizer.Client.GetAsync($"/api/v1/matches/{matchId}/presences", Ct));
        var queued = presences.GetProperty("waitingList").GetArrayLength();

        using var _ = new AssertionScope();
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError, $"codes: {Codes(responses)}");
        queued.Should().Be(10, $"10 players asked to queue at once, codes: {Codes(responses)}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreateMatchAsync(Actor organizer, DateTimeOffset when, int regularSlots, bool recurring, int opensHoursBefore)
    {
        object body = recurring
            ? new
            {
                name = "Racha", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
                maxPlayers = Math.Max(2, regularSlots), regularSlots, type = "Recurring", frequency = "Weekly",
                dayOfWeek = ((int)when.DayOfWeek + 6) % 7 + 1, confirmationOpensHoursBefore = opensHoursBefore,
            }
            : new
            {
                name = "Racha", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
                maxPlayers = Math.Max(2, regularSlots), regularSlots, type = "OneOff", confirmationOpensHoursBefore = opensHoursBefore,
            };
        var response = await organizer.Client.PostAsJsonAsync("/api/v1/matches", body, Ct);
        var json = await Read(response, HttpStatusCode.Created);
        return json.GetProperty("id").GetGuid();
    }

    /// <summary>The in-memory server rethrows unhandled exceptions; a real host would answer 500.</summary>
    private static async Task<HttpResponseMessage> Safe(Task<HttpResponseMessage> call)
    {
        try
        {
            return await call;
        }
        catch (Exception)
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private static async Task Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Codes(IEnumerable<HttpResponseMessage> responses) =>
        string.Join(",", responses.GroupBy(r => (int)r.StatusCode).Select(g => $"{g.Key}x{g.Count()}"));
}
