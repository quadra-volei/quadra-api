using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Quadra.IntegrationTests.Modules.Auth;

namespace Quadra.IntegrationTests.Modules.App;

/// <summary>The version gate is public and reads its values from configuration (DECISIONS #74).</summary>
public sealed class AppVersionTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public AppVersionTests(AuthWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anyone_reads_the_configured_versions()
    {
        using var host = _factory.WithWebHostBuilder(b => b
            .UseSetting("App:MinimumVersion", "1.2.0")
            .UseSetting("App:RecommendedVersion", " 1.3.0 "));

        var response = await host.CreateClient().GetAsync("/api/v1/app/version", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("minimumVersion").GetString().Should().Be("1.2.0");
        body.GetProperty("recommendedVersion").GetString().Should().Be("1.3.0");
        body.GetProperty("storeUrl").GetString().Should().Contain("com.quadra.app");
    }

    [Fact]
    public async Task Without_configuration_nothing_is_required()
    {
        var body = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/app/version", Ct);

        body.GetProperty("minimumVersion").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("recommendedVersion").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
