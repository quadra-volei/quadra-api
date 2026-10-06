using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="RefreshHandler"/> and <see cref="LogoutHandler"/> (FA.3). Covers the
/// lookup/revoke/expiry gate, mandatory rotation (old row revoked, new row persisted, new raw
/// token returned), and the concurrent-refresh guard.
/// </summary>
public sealed class RefreshHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private const string RawToken = "raw-refresh-token";

    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private RefreshHandler CreateSut() => new(
        _refreshTokens,
        _users,
        AuthTestSupport.SessionFactory(),
        new FixedTimeProvider(Now),
        NullLogger<RefreshHandler>.Instance);

    private LogoutHandler CreateLogoutSut() => new(
        _refreshTokens,
        new FixedTimeProvider(Now),
        NullLogger<LogoutHandler>.Instance);

    private static RefreshToken StoredToken(
        Guid userId,
        DateTimeOffset? expiresAt = null,
        string? deviceId = "dev-1") => RefreshToken.Issue(
            Guid.NewGuid(),
            userId,
            tokenHash: RefreshTokenHasher.Hash(RawToken),
            deviceId,
            issuedAt: Now.AddDays(-1),
            expiresAt: expiresAt ?? Now.AddDays(29));

    private User GivenUser()
    {
        var user = User.CreateForPhone(Guid.NewGuid(), "+5511999990000", Now.AddDays(-5));
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    private void GivenStored(RefreshToken? token) =>
        _refreshTokens.FindByTokenHashAsync(RefreshTokenHasher.Hash(RawToken), Arg.Any<CancellationToken>())
            .Returns(token);

    // ---------------- lookup gate ----------------

    /// <summary>
    /// Covers FA.3: "refresh token unknown → 401" (RefreshTokenRejectedException).
    /// </summary>
    [Fact]
    public async Task Unknown_token_is_rejected()
    {
        GivenStored(null);

        var act = async () => await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _refreshTokens.DidNotReceive().TryRotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "revoked refresh token → 401" — a rotated or logged-out token cannot be reused.
    /// </summary>
    [Fact]
    public async Task Revoked_token_is_rejected()
    {
        var user = GivenUser();
        var stored = StoredToken(user.Id);
        stored.Revoke(Now.AddHours(-1));
        GivenStored(stored);

        var act = async () => await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
        await _refreshTokens.DidNotReceive().TryRotateAsync(Arg.Any<RefreshToken>(), Arg.Any<RefreshToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "expired refresh token → 401".
    /// </summary>
    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var user = GivenUser();
        GivenStored(StoredToken(user.Id, expiresAt: Now));

        var act = async () => await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
    }

    /// <summary>
    /// Covers the orphan case: a token whose user row no longer exists is rejected.
    /// </summary>
    [Fact]
    public async Task Token_without_user_is_rejected()
    {
        GivenStored(StoredToken(Guid.NewGuid()));

        var act = async () => await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
    }

    // ---------------- rotation ----------------

    /// <summary>
    /// Covers FA.3: "refresh returns a new access token and rotates the refresh token" — the new
    /// raw token differs from the presented one, only its hash is persisted, the lifetime window
    /// restarts, and the device id carries over when the request omits it.
    /// </summary>
    [Fact]
    public async Task Valid_token_is_rotated_and_a_new_session_is_returned()
    {
        var user = GivenUser();
        var stored = StoredToken(user.Id);
        GivenStored(stored);
        _refreshTokens.TryRotateAsync(stored, Arg.Any<RefreshToken>(), Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var response = await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        response.UserId.Should().Be(user.Id);
        response.IsNewUser.Should().BeFalse();
        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBe(RawToken);

        await _refreshTokens.Received(1).TryRotateAsync(
            stored,
            Arg.Is<RefreshToken>(t =>
                t.TokenHash == RefreshTokenHasher.Hash(response.RefreshToken)
                && t.UserId == user.Id
                && t.DeviceId == "dev-1"
                && t.IssuedAt == Now
                && t.ExpiresAt == Now.AddDays(30)),
            Now,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers the concurrent-refresh guard: when another request rotated the same token first,
    /// this one is rejected instead of minting a second valid session.
    /// </summary>
    [Fact]
    public async Task Token_rotated_by_a_concurrent_request_is_rejected()
    {
        var user = GivenUser();
        var stored = StoredToken(user.Id);
        GivenStored(stored);
        _refreshTokens.TryRotateAsync(stored, Arg.Any<RefreshToken>(), Now, Arg.Any<CancellationToken>())
            .Returns(false);

        var act = async () => await CreateSut()
            .HandleAsync(RawToken, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RefreshTokenRejectedException>();
    }

    // ---------------- logout ----------------

    /// <summary>
    /// Covers logout: an active refresh token is revoked.
    /// </summary>
    [Fact]
    public async Task Logout_revokes_an_active_token()
    {
        var stored = StoredToken(Guid.NewGuid());
        GivenStored(stored);

        await CreateLogoutSut().HandleAsync(RawToken, TestContext.Current.CancellationToken);

        await _refreshTokens.Received(1).RevokeAsync(stored, Now, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers logout idempotency: an unknown token is a silent no-op.
    /// </summary>
    [Fact]
    public async Task Logout_with_unknown_token_does_nothing()
    {
        GivenStored(null);

        await CreateLogoutSut().HandleAsync(RawToken, TestContext.Current.CancellationToken);

        await _refreshTokens.DidNotReceive().RevokeAsync(Arg.Any<RefreshToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
