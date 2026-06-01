using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="RefreshHandler"/> (FA.3). Covers the lookup/revoke/expiry gate, the
/// Cognito REFRESH_TOKEN_AUTH exchange, rotation (revoke old + persist new), and that the refresh
/// leg never publishes <c>UserLoggedIn</c>.
/// </summary>
public sealed class RefreshHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private const string RawToken = "raw-refresh-token";
    private const string CognitoSub = "cognito-sub-1";

    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly ICognitoAuthClient _cognito = Substitute.For<ICognitoAuthClient>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(Now);

    private RefreshHandler CreateSut() => new(
        _refreshTokens,
        _cognito,
        _users,
        _timeProvider,
        NullLogger<RefreshHandler>.Instance);

    private static RefreshToken ActiveStoredToken(Guid? userId = null) => RefreshToken.Issue(
        Guid.NewGuid(),
        userId ?? Guid.NewGuid(),
        tokenHash: RefreshTokenHasher.Hash(RawToken),
        cognitoSub: CognitoSub,
        deviceId: null,
        issuedAt: Now.AddDays(-1),
        expiresAt: Now.AddDays(29));

    private static User ConfirmedUser(Guid id) =>
        User.CreateForExternal(id, CognitoSub, IdentityProvider.Google, "u@example.com", Now);

    // ---------------- lookup gate ----------------

    /// <summary>
    /// Covers FA.3: "refresh token unknown locally → 401" (RefreshTokenRejectedException).
    /// </summary>
    [Fact]
    public async Task Unknown_token_is_rejected()
    {
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _cognito.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "revoked (revoked_at not null) → 401".
    /// </summary>
    [Fact]
    public async Task Revoked_token_is_rejected()
    {
        var revoked = ActiveStoredToken();
        revoked.Revoke(Now.AddMinutes(-5));

        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(revoked);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _cognito.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "expired (expires_at in the past) → 401".
    /// </summary>
    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var expired = RefreshToken.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tokenHash: RefreshTokenHasher.Hash(RawToken),
            cognitoSub: CognitoSub,
            deviceId: null,
            issuedAt: Now.AddDays(-40),
            expiresAt: Now.AddDays(-10));

        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expired);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _cognito.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: the lookup hashes the supplied raw token (SHA-256) — never the raw value.
    /// </summary>
    [Fact]
    public async Task Lookup_uses_sha256_hash_of_supplied_token()
    {
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RefreshToken?)null);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<RefreshTokenRejectedException>();

        await _refreshTokens.Received(1).FindByTokenHashAsync(
            RefreshTokenHasher.Hash(RawToken), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().FindByTokenHashAsync(RawToken, Arg.Any<CancellationToken>());
    }

    // ---------------- Cognito rejection ----------------

    /// <summary>
    /// Covers FA.3: "On Cognito rejection (NotAuthorizedException): mark the local row revoked and
    /// return 401."
    /// </summary>
    [Fact]
    public async Task Cognito_rejection_revokes_local_row_and_rethrows_401()
    {
        var stored = ActiveStoredToken();
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RefreshTokenRejectedException("Cognito rejected"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _refreshTokens.Received(1).RevokeAsync(stored, Now, Arg.Any<CancellationToken>());
    }

    // ---------------- success: no rotation ----------------

    /// <summary>
    /// Covers FA.3: "returns a fresh access + ID token; the returned RefreshToken is the same one
    /// unless rotation is enabled" — rotation disabled keeps the row untouched (no revoke / no add).
    /// </summary>
    [Fact]
    public async Task Refresh_without_rotation_returns_same_token_and_does_not_rotate()
    {
        var userId = Guid.NewGuid();
        var stored = ActiveStoredToken(userId);
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _users.FindByCognitoSubAsync(CognitoSub, Arg.Any<CancellationToken>())
            .Returns(ConfirmedUser(userId));

        // Rotation disabled: Cognito returns null refresh token.
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("new-access", "new-id", RefreshToken: null, 3600, Now.AddDays(30), CognitoSub));

        var sut = CreateSut();

        var response = await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        response.AccessToken.Should().Be("new-access");
        response.IdToken.Should().Be("new-id");
        response.RefreshToken.Should().Be(RawToken, "the same token is returned when rotation is off");
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(3600);
        response.UserId.Should().Be(userId);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: rotation disabled — Cognito echoing the SAME refresh token also does not rotate.
    /// </summary>
    [Fact]
    public async Task Refresh_with_same_token_echoed_does_not_rotate()
    {
        var userId = Guid.NewGuid();
        var stored = ActiveStoredToken(userId);
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _users.FindByCognitoSubAsync(CognitoSub, Arg.Any<CancellationToken>())
            .Returns(ConfirmedUser(userId));
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a", "i", RawToken, 3600, Now.AddDays(30), CognitoSub));

        var sut = CreateSut();

        var response = await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        response.RefreshToken.Should().Be(RawToken);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().AddAsync(default!, Arg.Any<CancellationToken>());
    }

    // ---------------- success: rotation ----------------

    /// <summary>
    /// Covers FA.3: "If rotation is enabled and Cognito returns a new refresh token, mark the old
    /// row revoked_at = now() and persist a new refresh_tokens row." Also verifies the new row is
    /// stored as the SHA-256 hash of the new token (never the raw value).
    /// </summary>
    [Fact]
    public async Task Refresh_with_rotation_revokes_old_row_and_persists_new_hashed_row()
    {
        const string newRaw = "rotated-refresh-token";
        var userId = Guid.NewGuid();
        var stored = ActiveStoredToken(userId);
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _users.FindByCognitoSubAsync(CognitoSub, Arg.Any<CancellationToken>())
            .Returns(ConfirmedUser(userId));
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("acc", "idt", newRaw, 3600, Now.AddDays(30), CognitoSub));

        var sut = CreateSut();

        var response = await sut.HandleAsync(RawToken, deviceId: "device-9", TestContext.Current.CancellationToken);

        response.RefreshToken.Should().Be(newRaw, "the rotated token is returned to the client once");

        await _refreshTokens.Received(1).RevokeAsync(stored, Now, Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).AddAsync(
            Arg.Is<RefreshToken>(t =>
                t.TokenHash == RefreshTokenHasher.Hash(newRaw)
                && t.TokenHash != newRaw
                && t.UserId == userId
                && t.CognitoSub == CognitoSub
                && t.DeviceId == "device-9"
                && t.IssuedAt == Now
                && t.ExpiresAt == Now.AddDays(30)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "no UserLoggedIn event published on refresh" — RefreshHandler has no event
    /// publisher dependency, so a successful refresh cannot publish. Asserted structurally by the
    /// ctor signature (no IEventPublisher) and behaviorally by a clean success path.
    /// </summary>
    [Fact]
    public async Task Refresh_success_does_not_publish_event()
    {
        var userId = Guid.NewGuid();
        var stored = ActiveStoredToken(userId);
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _users.FindByCognitoSubAsync(CognitoSub, Arg.Any<CancellationToken>())
            .Returns(ConfirmedUser(userId));
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a", "i", RefreshToken: null, 3600, Now.AddDays(30), CognitoSub));

        var ctorParams = typeof(RefreshHandler).GetConstructors()[0].GetParameters();

        var sut = CreateSut();
        var response = await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        response.Should().NotBeNull();
        ctorParams.Should().NotContain(
            p => p.ParameterType == typeof(Quadra.Infrastructure.Messaging.IEventPublisher),
            "the refresh leg must not be able to publish UserLoggedIn");
    }

    /// <summary>
    /// Covers FA.3: stored row's Cognito sub exists locally but no matching users row → 401.
    /// </summary>
    [Fact]
    public async Task Missing_local_user_is_rejected()
    {
        var stored = ActiveStoredToken();
        _refreshTokens.FindByTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(stored);
        _cognito.RefreshAsync(RawToken, CognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a", "i", null, 3600, Now.AddDays(30), CognitoSub));
        _users.FindByCognitoSubAsync(CognitoSub, Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
