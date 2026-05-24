using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// End-to-end tests for FA.1 — JWT Validation Middleware. Exercises the full ASP.NET pipeline
/// using <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> with
/// an in-process RSA signing key (no Cognito network round-trip).
/// </summary>
public sealed class JwtMiddlewareTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public JwtMiddlewareTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static string Bearer(string token) => token;

    /// <summary>
    /// Covers acceptance criterion #5 — /health remains reachable anonymously.
    /// </summary>
    [Fact]
    public async Task Health_endpoint_is_reachable_without_token()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers acceptance criterion #3 / #5 — protected route without a token returns 401
    /// with the standardized JSON error body.
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
    /// Covers acceptance criteria #1, #2, #5 — a valid token signed by the trusted key is accepted
    /// and HttpContext.User is populated (claims sub→NameIdentifier and custom:role→Role).
    /// </summary>
    [Fact]
    public async Task Protected_route_with_valid_token_returns_200_and_populates_claims()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience,
            subject: "abc-123",
            email: "alice@example.com",
            role: "player",
            cognitoUsername: "alice");

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Bearer(token));

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        body.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        body.GetProperty("nameIdentifier").GetString().Should().Be("abc-123");
        body.GetProperty("email").GetString().Should().Be("alice@example.com");
        body.GetProperty("role").GetString().Should().Be("player");
        body.GetProperty("cognitoUsername").GetString().Should().Be("alice");
    }

    /// <summary>
    /// Covers acceptance criterion #3 — malformed tokens are rejected with 401.
    /// </summary>
    [Fact]
    public async Task Malformed_token_returns_401()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("error").GetString().Should().Be("unauthorized");
    }

    /// <summary>
    /// Covers acceptance criterion #3 — expired tokens are rejected with 401.
    /// </summary>
    [Fact]
    public async Task Expired_token_returns_401()
    {
        var now = DateTime.UtcNow;
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience,
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1));

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers acceptance criterion #5 — tokens with a client_id that does not match the configured
    /// Audience are rejected (since Cognito access tokens validate client_id instead of aud).
    /// </summary>
    [Fact]
    public async Task Token_with_mismatched_client_id_returns_401()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: "some-other-client-id");

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers acceptance criterion #1 — tokens with an issuer different from the configured
    /// Cognito authority are rejected.
    /// </summary>
    [Fact]
    public async Task Token_with_wrong_issuer_returns_401()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: "https://evil.example.com/some-pool",
            clientId: AuthWebApplicationFactory.TestAudience);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers acceptance criterion #1 — tokens signed by an untrusted key are rejected.
    /// </summary>
    [Fact]
    public async Task Token_signed_by_untrusted_key_returns_401()
    {
        using var rogueRsa = System.Security.Cryptography.RSA.Create(2048);
        var rogueKey = new RsaSecurityKey(rogueRsa) { KeyId = "rogue-key" };
        var creds = new SigningCredentials(rogueKey, SecurityAlgorithms.RsaSha256);

        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience,
            signingCredentials: creds);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-protected", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers acceptance criterion #5 — SignalR /hubs/* paths accept the bearer token from
    /// the access_token query string.
    /// </summary>
    [Fact]
    public async Task Hubs_path_accepts_token_from_query_string()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience);

        using var client = CreateClient();

        using var response = await client.GetAsync(
            $"/hubs/test?access_token={Uri.EscapeDataString(token)}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers acceptance criterion #5 — /hubs/* still rejects when no token is supplied
    /// (neither header nor query string).
    /// </summary>
    [Fact]
    public async Task Hubs_path_without_token_returns_401()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/hubs/test", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers acceptance criterion #2 — custom:role is mapped to ClaimTypes.Role,
    /// enabling [Authorize(Roles = "admin")] to function.
    /// </summary>
    [Fact]
    public async Task Authorize_with_role_admin_allows_admin_token()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience,
            role: "admin");

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-admin", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers acceptance criterion #2 — non-admin role is denied by [Authorize(Roles = "admin")]
    /// (403 because the token is authenticated but the role check fails).
    /// </summary>
    [Fact]
    public async Task Authorize_with_role_admin_denies_non_admin_token()
    {
        var token = TestJwtFactory.CreateAccessToken(
            issuer: AuthWebApplicationFactory.TestIssuer,
            clientId: AuthWebApplicationFactory.TestAudience,
            role: "player");

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/test-admin", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
