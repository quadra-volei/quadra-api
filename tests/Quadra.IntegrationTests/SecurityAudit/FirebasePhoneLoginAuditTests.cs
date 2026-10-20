using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Oidc;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Firebase Phone Auth (DECISIONS #78): the Firebase-verified phone logs into the same account as
/// before; a refused token gets 401; with the Firebase provider the fixed-code route is off.
/// The token check itself is unit-tested (FirebasePhoneTokenValidatorTests); here it is stubbed.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class FirebasePhoneLoginAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;
    private readonly IFirebasePhoneTokenValidator _firebase = Substitute.For<IFirebasePhoneTokenValidator>();

    public FirebasePhoneLoginAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Host(string provider = "Fake") =>
        _fx.Create("Testing", new Dictionary<string, string?>
            {
                ["Auth:PhoneVerification:Provider"] = provider,
                ["Auth:PhoneVerification:Firebase:ProjectId"] = "quadra-volei-app",
            })
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(_firebase)));

    [Fact]
    public async Task A_firebase_verified_phone_logs_into_the_same_account()
    {
        var existing = await _fx.LoginAsync();
        _firebase.ValidateAsync("good-token", Arg.Any<CancellationToken>()).Returns(existing.Phone);
        using var host = Host();

        var response = await host.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login/firebase-phone", new { idToken = "good-token", deviceId = "pixel" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("userId").GetGuid().Should().Be(existing.Id);
        body.GetProperty("isNewUser").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_firebase_token_gets_401()
    {
        _firebase.ValidateAsync("bad-token", Arg.Any<CancellationToken>()).ThrowsAsync(new OidcTokenInvalidException("nope"));
        using var host = Host();

        (await host.CreateClient().PostAsJsonAsync("/api/v1/auth/login/firebase-phone", new { idToken = "bad-token" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.CreateClient().PostAsJsonAsync("/api/v1/auth/login/firebase-phone", new { idToken = "" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_the_firebase_provider_the_fixed_code_route_is_off()
    {
        _firebase.ValidateAsync("new-phone", Arg.Any<CancellationToken>()).Returns("+5511977770001");
        using var host = Host(provider: "Firebase");
        var client = host.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/auth/login/sms-otp",
                new { step = "verify", phoneNumber = "+5511977770002", code = TestJwtFactory.FakeOtpCode }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PostAsJsonAsync("/api/v1/auth/login/firebase-phone", new { idToken = "new-phone" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
