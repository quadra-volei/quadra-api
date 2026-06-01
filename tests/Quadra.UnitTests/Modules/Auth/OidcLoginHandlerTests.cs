using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="OidcLoginHandler"/> (FA.3 Google/Apple login). Covers token validation,
/// the Cognito broker, refresh-token persistence (hashed), exception translation, and the
/// <c>UserLoggedIn</c> event.
/// </summary>
public sealed class OidcLoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IOidcTokenValidator _oidc = Substitute.For<IOidcTokenValidator>();
    private readonly ICognitoAuthClient _cognito = Substitute.For<ICognitoAuthClient>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(Now);

    private OidcLoginHandler CreateSut() => new(
        _oidc,
        _cognito,
        _users,
        _refreshTokens,
        _publisher,
        _timeProvider,
        NullLogger<OidcLoginHandler>.Instance);

    private static User ExternalUser(IdentityProvider provider, string cognitoSub) =>
        User.CreateForExternal(Guid.NewGuid(), cognitoSub, provider, "u@example.com", Now);

    // ---------------- google happy path ----------------

    /// <summary>
    /// Covers FA.3: "POST /login/google — validate token → broker Cognito session → tokens";
    /// "returns JWT + refresh token (AuthTokensResponse shape)"; refresh token persisted hashed;
    /// "UserLoggedIn event published on login".
    /// </summary>
    [Fact]
    public async Task Google_login_validates_brokers_persists_hashed_token_and_publishes_event()
    {
        const string idToken = "good.google.token";
        const string externalSub = "google-sub-1";
        const string cognitoSub = "cog-sub-google";
        const string rawRefresh = "raw-refresh-google";

        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(externalSub, "u@example.com", true));
        _cognito.BrokerExternalSessionAsync(IdentityProvider.Google, externalSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("acc", "idt", rawRefresh, 3600, Now.AddDays(30), cognitoSub));
        var user = ExternalUser(IdentityProvider.Google, cognitoSub);
        _users.FindByCognitoSubAsync(cognitoSub, Arg.Any<CancellationToken>()).Returns(user);

        var sut = CreateSut();

        var response = await sut.HandleAsync(
            IdentityProvider.Google, idToken, deviceId: "dev-1", TestContext.Current.CancellationToken);

        response.AccessToken.Should().Be("acc");
        response.IdToken.Should().Be("idt");
        response.RefreshToken.Should().Be(rawRefresh);
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(3600);
        response.UserId.Should().Be(user.Id);
        response.CognitoSub.Should().Be(cognitoSub);
        response.ConfirmationStatus.Should().Be("Confirmed");

        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t =>
                t.TokenHash == RefreshTokenHasher.Hash(rawRefresh)
                && t.TokenHash != rawRefresh
                && t.UserId == user.Id
                && t.CognitoSub == cognitoSub
                && t.DeviceId == "dev-1"
                && t.IssuedAt == Now
                && t.ExpiresAt == Now.AddDays(30)),
            Arg.Any<CancellationToken>());

        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e =>
                e.UserId == user.Id
                && e.CognitoSub == cognitoSub
                && e.Provider == "google"
                && e.DeviceId == "dev-1"
                && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "POST /login/apple (idem, Apple provider)" — validated with OidcProvider.Apple
    /// and event Provider == "apple".
    /// </summary>
    [Fact]
    public async Task Apple_login_validates_with_apple_provider_and_publishes_apple_event()
    {
        const string idToken = "good.apple.token";
        const string externalSub = "apple-sub-1";
        const string cognitoSub = "cog-sub-apple";

        _oidc.ValidateAsync(idToken, OidcProvider.Apple, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(externalSub, "relay@privaterelay.appleid.com", true));
        _cognito.BrokerExternalSessionAsync(IdentityProvider.Apple, externalSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a", "i", "rr", 3600, Now.AddDays(30), cognitoSub));
        var user = ExternalUser(IdentityProvider.Apple, cognitoSub);
        _users.FindByCognitoSubAsync(cognitoSub, Arg.Any<CancellationToken>()).Returns(user);

        var sut = CreateSut();

        await sut.HandleAsync(IdentityProvider.Apple, idToken, deviceId: null, TestContext.Current.CancellationToken);

        await _oidc.Received(1).ValidateAsync(idToken, OidcProvider.Apple, Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.Provider == "apple"), Arg.Any<CancellationToken>());
    }

    // ---------------- error paths ----------------

    /// <summary>
    /// Covers FA.3: "401 — the provider ID token fails validation" — never reaches Cognito/persist.
    /// </summary>
    [Fact]
    public async Task Invalid_token_throws_401_without_broker_or_persist()
    {
        const string idToken = "bad.token";
        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("bad signature"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            IdentityProvider.Google, idToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OidcTokenInvalidException>();
        await _cognito.DidNotReceiveWithAnyArgs().BrokerExternalSessionAsync(default, default!, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserLoggedIn>(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "404 — token valid but no linked Cognito user" (broker throws UserNotFound).
    /// </summary>
    [Fact]
    public async Task No_linked_cognito_user_throws_404()
    {
        const string idToken = "good.token";
        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("ext-sub", "u@example.com", true));
        _cognito.BrokerExternalSessionAsync(IdentityProvider.Google, "ext-sub", Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserNotFoundForLoginException("no linked user"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            IdentityProvider.Google, idToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UserNotFoundForLoginException>();
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserLoggedIn>(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "404 — no local users row linked to the brokered Cognito sub".
    /// </summary>
    [Fact]
    public async Task No_local_user_for_brokered_sub_throws_404()
    {
        const string idToken = "good.token";
        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("ext-sub", "u@example.com", true));
        _cognito.BrokerExternalSessionAsync(IdentityProvider.Google, "ext-sub", Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a", "i", "rr", 3600, Now.AddDays(30), "cog-sub"));
        _users.FindByCognitoSubAsync("cog-sub", Arg.Any<CancellationToken>()).Returns((User?)null);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            IdentityProvider.Google, idToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UserNotFoundForLoginException>();
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "502 — unexpected Cognito service error" propagates from the broker.
    /// </summary>
    [Fact]
    public async Task Cognito_unavailable_propagates_502()
    {
        const string idToken = "good.token";
        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("ext-sub", "u@example.com", true));
        _cognito.BrokerExternalSessionAsync(IdentityProvider.Google, "ext-sub", Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoUnavailableException("boom"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            IdentityProvider.Google, idToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CognitoUnavailableException>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
