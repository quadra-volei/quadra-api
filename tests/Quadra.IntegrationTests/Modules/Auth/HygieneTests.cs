using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// SEC-15: an endpoint that forgets [Authorize] still requires a signed-in user, and an
/// unhandled error answers a ProblemDetails 500 with no internals.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class HygieneTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public HygieneTests(AuthWebApplicationFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient SignedIn()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.CreateAccessToken(subject: Guid.NewGuid().ToString()));
        return client;
    }

    [Fact]
    public async Task An_endpoint_without_authorize_still_requires_a_user()
    {
        (await _factory.CreateClient().GetAsync("/test-unattributed", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SignedIn().GetAsync("/test-unattributed", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unhandled_error_answers_problem_details_without_internals()
    {
        var response = await SignedIn().GetAsync("/test-unattributed/boom", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        body.Should().NotContain("secret internal detail").And.NotContain("InvalidOperationException");
    }
}
