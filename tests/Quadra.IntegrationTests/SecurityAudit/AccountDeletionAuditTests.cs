using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Quadra.IntegrationTests.Modules.Auth;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// The person can export and delete their account (audit SEC-14, LGPD): export returns only the
/// caller's data; deletion ends every session, removes the profile and posts, cancels the
/// upcoming matches they organize, and the same phone later starts a brand-new account.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class AccountDeletionAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public AccountDeletionAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Export_returns_the_callers_own_data()
    {
        var user = await _fx.LoginAsync();
        var post = await Read(await user.Client.PostAsJsonAsync("/api/v1/posts", new { text = "meu post" }, Ct), HttpStatusCode.Created);

        var export = await Read(await user.Client.GetAsync("/api/v1/auth/me/export", Ct));

        export.GetProperty("account").GetProperty("id").GetGuid().Should().Be(user.Id);
        export.GetProperty("account").GetProperty("phoneNumber").GetString().Should().Be(user.Phone);
        export.GetProperty("profile").GetProperty("posts").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).Should().Contain(post.GetProperty("id").GetGuid());
        foreach (var section in new[] { "matches", "games", "ranking" })
        {
            export.TryGetProperty(section, out _).Should().BeTrue(section);
        }
    }

    [Fact]
    public async Task Deleting_the_account_ends_sessions_and_removes_the_person()
    {
        var user = await _fx.LoginAsync();
        var other = await _fx.LoginAsync();
        var post = await Read(await user.Client.PostAsJsonAsync("/api/v1/posts", new { text = "vou sumir" }, Ct), HttpStatusCode.Created);
        var matchId = await CreateMatchAsync(user);
        (await other.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();
        (await other.Client.GetAsync($"/api/v1/profiles/{user.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await user.Client.DeleteAsync("/api/v1/auth/me", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await user.Client.GetAsync("/api/v1/auth/me", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.Client.GetAsync($"/api/v1/profiles/{user.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Client.GetStringAsync("/api/v1/posts", Ct)).Should().NotContain(post.GetProperty("id").GetString());
        (await Read(await other.Client.GetAsync($"/api/v1/matches/{matchId}", Ct))).GetProperty("status").GetString()
            .Should().Be("Cancelled", "an upcoming match of a deleted organizer is cancelled");

        // The same phone number starts over with a new, empty account.
        var again = await _fx.Factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = user.Phone, code = TestJwtFactory.FakeOtpCode }, Ct);
        var body = await Read(again);
        body.GetProperty("userId").GetGuid().Should().NotBe(user.Id);
        body.GetProperty("isNewUser").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_callers_cannot_export_or_delete()
    {
        var anon = _fx.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/auth/me/export", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.DeleteAsync("/api/v1/auth/me", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreateMatchAsync(Actor organizer)
    {
        var when = DateTimeOffset.UtcNow.AddDays(3);
        var response = await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
            maxPlayers = 12, regularSlots = 12, type = "OneOff", confirmationOpensHoursBefore = 96,
        }, Ct);
        return (await Read(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
