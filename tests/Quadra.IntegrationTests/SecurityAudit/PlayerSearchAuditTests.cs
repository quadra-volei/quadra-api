using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Player search (DECISIONS #75): by @handle prefix only, never by name; blocked players are not
/// found either way; rate limited per account.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class PlayerSearchAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public PlayerSearchAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task SetHandleAsync(Actor user, string firstName, string handle) =>
        (await user.Client.PutAsJsonAsync("/api/v1/profiles/me", new
        {
            firstName, lastName = "Teste", handle, birthDate = "1995-05-10", position = "LEV",
            modality = "Indoor", level = "Intermediate", photoObjectKey = (string?)null,
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task<IReadOnlyList<string>> SearchAsync(Actor user, string query)
    {
        var response = await user.Client.GetAsync($"/api/v1/profiles/search?handle={Uri.EscapeDataString(query)}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("handle").GetString()!).ToList();
    }

    [Fact]
    public async Task Players_are_found_by_handle_prefix_only_and_never_across_a_block()
    {
        var ana = await _fx.LoginAsync();
        var bruno = await _fx.LoginAsync();
        var searcher = await _fx.LoginAsync();
        await SetHandleAsync(ana, "Mariana", "ana_quadra1");
        await SetHandleAsync(bruno, "Bruno", "ana_quadra2");

        (await SearchAsync(searcher, "@ANA_QUA")).Should().Contain(["ana_quadra1", "ana_quadra2"]);
        (await SearchAsync(searcher, "mariana")).Should().BeEmpty("a real name is not searchable");

        (await ana.Client.PutAsync($"/api/v1/profiles/{searcher.Id}/block", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SearchAsync(searcher, "ana_qua")).Should().Equal("ana_quadra2");
        (await SearchAsync(ana, "ana_qua")).Should().Contain("ana_quadra2");
    }

    [Fact]
    public async Task Bad_queries_and_floods_are_refused()
    {
        var user = await _fx.LoginAsync();
        (await user.Client.GetAsync("/api/v1/profiles/search?handle=a", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await user.Client.GetAsync("/api/v1/profiles/search?handle=ana%20maria", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _fx.Factory.CreateClient().GetAsync("/api/v1/profiles/search?handle=ana", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 35; i++)
        {
            codes.Add((await user.Client.GetAsync("/api/v1/profiles/search?handle=zz", Ct)).StatusCode);
        }

        codes.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
