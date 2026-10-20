using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="OidcLoginHandler"/> (FA.3 Google/Apple login). Covers ID token
/// validation, find-or-create of the local user by external identity, hashed refresh-token
/// persistence, and the <c>UserRegistered</c> / <c>UserLoggedIn</c> events.
/// </summary>
public sealed class OidcLoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private const string IdToken = "good.google.token";
    private const string GoogleSub = "google-sub-1";

    private readonly IOidcTokenValidator _oidc = Substitute.For<IOidcTokenValidator>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();

    private OidcLoginHandler CreateSut() => new(
        _oidc,
        new UserProvisioner(_users, _publisher, NullLogger<UserProvisioner>.Instance),
        AuthTestSupport.SessionFactory(),
        _refreshTokens,
        _publisher,
        new FixedTimeProvider(Now),
        NullLogger<OidcLoginHandler>.Instance);

    // ---------------- first login creates the user ----------------

    /// <summary>
    /// Covers FA.3: "POST /login/google — validate token → create user → JWT + refresh". The
    /// Google <c>sub</c> is stored as the external identity and the email is normalized.
    /// </summary>
    [Fact]
    public async Task Google_first_login_creates_user_and_issues_session()
    {
        _oidc.ValidateAsync(IdToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(GoogleSub, "  U@Example.com ", true));
        _users.FindByExternalIdentityAsync(IdentityProvider.Google, GoogleSub, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _users.TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(true);

        var response = await CreateSut().HandleAsync(
            IdentityProvider.Google, IdToken, deviceId: "dev-1", TestContext.Current.CancellationToken);

        response.IsNewUser.Should().BeTrue();
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(15 * 60);

        await _users.Received(1).TryAddAsync(
            Arg.Is<User>(u =>
                u.Id == response.UserId
                && u.Provider == IdentityProvider.Google
                && u.ExternalSubject == GoogleSub
                && u.Email == "u@example.com"
                && u.PhoneNumber == null),
            Arg.Any<CancellationToken>());

        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t =>
                t.TokenHash == RefreshTokenHasher.Hash(response.RefreshToken)
                && t.TokenHash != response.RefreshToken
                && t.UserId == response.UserId
                && t.DeviceId == "dev-1"
                && t.IssuedAt == Now
                && t.ExpiresAt == Now.AddDays(30)),
            Arg.Any<CancellationToken>());

        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e =>
                e.UserId == response.UserId && e.Provider == "google" && e.Email == "u@example.com"),
            Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e =>
                e.UserId == response.UserId && e.Provider == "google" && e.DeviceId == "dev-1" && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    // ---------------- returning user ----------------

    /// <summary>
    /// Covers FA.3: a returning Google account logs into its existing user — no insert, no
    /// UserRegistered, IsNewUser false.
    /// </summary>
    [Fact]
    public async Task Google_login_for_known_identity_reuses_the_user()
    {
        var existing = User.CreateForExternal(
            Guid.NewGuid(), IdentityProvider.Google, GoogleSub, "u@example.com", Now.AddDays(-3));
        _oidc.ValidateAsync(IdToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(GoogleSub, "u@example.com", true));
        _users.FindByExternalIdentityAsync(IdentityProvider.Google, GoogleSub, Arg.Any<CancellationToken>())
            .Returns(existing);

        var response = await CreateSut().HandleAsync(
            IdentityProvider.Google, IdToken, deviceId: null, TestContext.Current.CancellationToken);

        response.UserId.Should().Be(existing.Id);
        response.IsNewUser.Should().BeFalse();
        await _users.DidNotReceive().TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "POST /login/apple (idem, Apple provider)" — validated with OidcProvider.Apple,
    /// looked up under the Apple provider, and the event Provider is "apple".
    /// </summary>
    [Fact]
    public async Task Apple_login_uses_the_apple_provider()
    {
        const string appleSub = "apple-sub-1";
        var existing = User.CreateForExternal(Guid.NewGuid(), IdentityProvider.Apple, appleSub, null, Now);
        _oidc.ValidateAsync("apple.token", OidcProvider.Apple, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(appleSub, null, false));
        _users.FindByExternalIdentityAsync(IdentityProvider.Apple, appleSub, Arg.Any<CancellationToken>())
            .Returns(existing);

        await CreateSut().HandleAsync(
            IdentityProvider.Apple, "apple.token", deviceId: null, TestContext.Current.CancellationToken);

        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == existing.Id && e.Provider == "apple"),
            Arg.Any<CancellationToken>());
    }

    // ---------------- failures ----------------

    /// <summary>
    /// Covers FA.3: "invalid ID token → 401" — the exception propagates and nothing is created.
    /// </summary>
    [Fact]
    public async Task Invalid_id_token_propagates_and_creates_nothing()
    {
        _oidc.ValidateAsync(Arg.Any<string>(), Arg.Any<OidcProvider>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("bad signature"));

        var act = async () => await CreateSut().HandleAsync(
            IdentityProvider.Google, "bad.token", deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OidcTokenInvalidException>();
        await _users.DidNotReceive().TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers the guard: the phone provider is not an OIDC provider.
    /// </summary>
    [Fact]
    public async Task Phone_provider_is_rejected()
    {
        var act = async () => await CreateSut().HandleAsync(
            IdentityProvider.Phone, IdToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
