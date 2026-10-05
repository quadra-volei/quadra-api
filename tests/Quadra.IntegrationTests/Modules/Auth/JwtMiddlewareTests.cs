using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// End-to-end tests for FA.1 — JWT Validation Middleware. Exercises the full ASP.NET pipeline
/// using <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> with
/// tokens signed locally using the test <c>Auth:Jwt</c> key (no network, no database).
/// </summary>
public sealed class JwtMiddlewareTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public JwtMiddlewareTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private HttpClient CreateClient(string bearerToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    /// <summary>
    /// Covers FA.1 — /health remains reachable anonymously.
    /// </summary>
    [Fact]
    public async Task Health_endpoint_is_reachable_without_token()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers FA.1 — protected route without a token returns 401 with the standardized JSON
    /// error body.
    /// </summary>
    [Fact]
    public async Task Protected_route_without_token_returns_401_with_standard_body()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("error").GetString().Should().Be("unauthorized");
        body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Covers FA.1 — a token signed with the configured key is accepted and HttpContext.User is
    /// populated (claims sub→NameIdentifier and role→Role).
    /// </summary>
    [Fact]
    public async Task Protected_route_with_valid_token_returns_200_and_populates_claims()
    {
        var token = TestJwtFactory.CreateAccessToken(
            subject: "abc-123",
            email: "alice@example.com",
            role: "player");

        using var client = CreateClient(token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        body.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        body.GetProperty("nameIdentifier").GetString().Should().Be("abc-123");
        body.GetProperty("email").GetString().Should().Be("alice@example.com");
        body.GetProperty("role").GetString().Should().Be("player");
    }

    /// <summary>
    /// Covers FA.1 — malformed tokens are rejected with 401.
    /// </summary>
    [Fact]
    public async Task Malformed_token_returns_401()
    {
        using var client = CreateClient("not.a.jwt");

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("error").GetString().Should().Be("unauthorized");
    }

    /// <summary>
    /// Covers FA.1 — expired tokens are rejected with 401.
    /// </summary>
    [Fact]
    public async Task Expired_token_returns_401()
    {
        var now = DateTime.UtcNow;
        var token = TestJwtFactory.CreateAccessToken(
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1));

        using var client = CreateClient(token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — tokens issued for a different audience are rejected.
    /// </summary>
    [Fact]
    public async Task Token_with_wrong_audience_returns_401()
    {
        var token = TestJwtFactory.CreateAccessToken(audience: "some-other-app");

        using var client = CreateClient(token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — tokens with an issuer different from the configured one are rejected.
    /// </summary>
    [Fact]
    public async Task Token_with_wrong_issuer_returns_401()
    {
        var token = TestJwtFactory.CreateAccessToken(issuer: "https://evil.example.com");

        using var client = CreateClient(token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — tokens signed with a key other than the configured one are rejected.
    /// </summary>
    [Fact]
    public async Task Token_signed_by_untrusted_key_returns_401()
    {
        var token = TestJwtFactory.CreateAccessToken(
            signingKey: "a-rogue-signing-key-that-is-also-32-bytes-long!!");

        using var client = CreateClient(token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — an unsigned token (<c>alg: none</c>) is rejected.
    /// </summary>
    [Fact]
    public async Task Unsigned_token_returns_401()
    {
        var signed = TestJwtFactory.CreateAccessToken();
        var payload = signed.Split('.')[1];
        // {"alg":"none","typ":"JWT"}
        var unsigned = $"eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.{payload}.";

        using var client = CreateClient(unsigned);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — SignalR /hubs/* paths accept the bearer token from the access_token query
    /// string.
    /// </summary>
    [Fact]
    public async Task Hubs_path_accepts_token_from_query_string()
    {
        var token = TestJwtFactory.CreateAccessToken();

        using var client = CreateClient();

        using var response = await client.GetAsync(
            $"/hubs/test?access_token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers FA.1 — /hubs/* still rejects when no token is supplied (neither header nor query
    /// string), and the query-string token is ignored outside /hubs/*.
    /// </summary>
    [Fact]
    public async Task Query_string_token_is_only_honoured_on_hubs_paths()
    {
        var token = TestJwtFactory.CreateAccessToken();
        using var client = CreateClient();

        using var hubs = await client.GetAsync("/hubs/test", TestContext.Current.CancellationToken);
        using var other = await client.GetAsync(
            $"/test-protected?access_token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);

        hubs.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        other.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.1 — the role claim is mapped to ClaimTypes.Role, enabling
    /// [Authorize(Roles = "admin")] to function.
    /// </summary>
    [Fact]
    public async Task Authorize_with_role_admin_allows_admin_token()
    {
        using var client = CreateClient(TestJwtFactory.CreateAccessToken(role: "admin"));

        using var response = await client.GetAsync("/test-admin", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers FA.1 — non-admin role is denied by [Authorize(Roles = "admin")] (403 because the
    /// token is authenticated but the role check fails).
    /// </summary>
    [Fact]
    public async Task Authorize_with_role_admin_denies_non_admin_token()
    {
        using var client = CreateClient(TestJwtFactory.CreateAccessToken(role: "player"));

        using var response = await client.GetAsync("/test-admin", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
