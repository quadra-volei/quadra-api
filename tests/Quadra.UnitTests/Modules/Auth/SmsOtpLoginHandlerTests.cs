using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.PhoneVerification;
using Quadra.Shared.Events.Auth;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="SmsOtpLoginHandler"/> (FA.3 SMS OTP login). Covers code delivery for
/// any phone number, the code check outcomes, account creation on the first valid OTP, hashed
/// refresh-token persistence, and the <c>UserRegistered</c> / <c>UserLoggedIn</c> events.
/// </summary>
public sealed class SmsOtpLoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private const string Phone = "+5511999990000";
    private const string Code = "123456";

    private readonly IPhoneVerificationService _phoneVerification = Substitute.For<IPhoneVerificationService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();

    private SmsOtpLoginHandler CreateSut() => new(
        _phoneVerification,
        new UserProvisioner(_users, _publisher, NullLogger<UserProvisioner>.Instance),
        AuthTestSupport.SessionFactory(),
        _refreshTokens,
        _publisher,
        new FixedTimeProvider(Now),
        NullLogger<SmsOtpLoginHandler>.Instance);

    private void GivenCheckReturns(PhoneVerificationResult result) =>
        _phoneVerification.CheckAsync(Phone, Code, Arg.Any<CancellationToken>()).Returns(result);

    // ---------------- initiate ----------------

    /// <summary>
    /// Covers FA.3: "initiate sends a code to any phone number" — no user lookup, so an unknown
    /// number gets the same 200 as a known one (the old 404 is gone).
    /// </summary>
    [Fact]
    public async Task Initiate_sends_code_without_looking_up_the_user_and_masks_the_destination()
    {
        var sut = CreateSut();

        var response = await sut.InitiateAsync(Phone, TestContext.Current.CancellationToken);

        await _phoneVerification.Received(1).StartAsync(Phone, Arg.Any<CancellationToken>());
        await _users.DidNotReceive().FindByPhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        response.Delivery.DeliveryMedium.Should().Be("SMS");
        response.Delivery.DeliveryDestination.Should().Be("+55*********00");
    }

    /// <summary>
    /// Covers FA.3: provider throttling on initiate propagates (controller maps it to 429).
    /// </summary>
    [Fact]
    public async Task Initiate_propagates_throttling()
    {
        _phoneVerification.StartAsync(Phone, Arg.Any<CancellationToken>())
            .ThrowsAsync(new PhoneVerificationThrottledException("slow down"));

        var act = async () => await CreateSut().InitiateAsync(Phone, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<PhoneVerificationThrottledException>();
    }

    // ---------------- verify: first login creates the user ----------------

    /// <summary>
    /// Covers FA.3: "the first valid OTP creates the user" — row inserted, session issued with
    /// IsNewUser = true, refresh token stored hashed, UserRegistered + UserLoggedIn published.
    /// </summary>
    [Fact]
    public async Task Verify_for_unknown_phone_creates_user_and_issues_session()
    {
        GivenCheckReturns(PhoneVerificationResult.Approved);
        _users.FindByPhoneNumberAsync(Phone, Arg.Any<CancellationToken>()).Returns((User?)null);
        _users.TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(true);

        var response = await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: "dev-1", TestContext.Current.CancellationToken);

        response.IsNewUser.Should().BeTrue();
        response.TokenType.Should().Be("Bearer");
        response.ExpiresIn.Should().Be(15 * 60);
        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();

        await _users.Received(1).TryAddAsync(
            Arg.Is<User>(u =>
                u.Id == response.UserId
                && u.Provider == IdentityProvider.Phone
                && u.PhoneNumber == Phone
                && u.ExternalSubject == null
                && u.CreatedAt == Now),
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
                e.UserId == response.UserId && e.Provider == "phone" && e.PhoneNumber == Phone && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e =>
                e.UserId == response.UserId && e.Provider == "phone" && e.DeviceId == "dev-1" && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    // ---------------- verify: returning user ----------------

    /// <summary>
    /// Covers FA.3: a returning phone logs into its existing account — no insert, IsNewUser false,
    /// no UserRegistered, but UserLoggedIn is published.
    /// </summary>
    [Fact]
    public async Task Verify_for_known_phone_reuses_the_user()
    {
        GivenCheckReturns(PhoneVerificationResult.Approved);
        var existing = User.CreateForPhone(Guid.NewGuid(), Phone, Now.AddDays(-10));
        _users.FindByPhoneNumberAsync(Phone, Arg.Any<CancellationToken>()).Returns(existing);

        var response = await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: null, TestContext.Current.CancellationToken);

        response.UserId.Should().Be(existing.Id);
        response.IsNewUser.Should().BeFalse();
        await _users.DidNotReceive().TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).PublishAsync(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers the unique-index race: two first logins for one phone — the loser adopts the row the
    /// winner inserted instead of failing or duplicating the account.
    /// </summary>
    [Fact]
    public async Task Verify_when_a_concurrent_login_created_the_user_adopts_that_row()
    {
        GivenCheckReturns(PhoneVerificationResult.Approved);
        var winner = User.CreateForPhone(Guid.NewGuid(), Phone, Now);
        _users.FindByPhoneNumberAsync(Phone, Arg.Any<CancellationToken>()).Returns((User?)null, winner);
        _users.TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: null, TestContext.Current.CancellationToken);

        response.UserId.Should().Be(winner.Id);
        response.IsNewUser.Should().BeFalse();
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    // ---------------- verify: rejected codes ----------------

    /// <summary>
    /// Covers FA.3: "wrong code → 401" (InvalidOtpException) and nothing is created or issued.
    /// </summary>
    [Fact]
    public async Task Verify_with_wrong_code_throws_and_creates_nothing()
    {
        GivenCheckReturns(PhoneVerificationResult.InvalidCode);

        var act = async () => await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOtpException>();
        await _users.DidNotReceive().TryAddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive().PublishAsync(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "expired / unknown verification → 401" (OtpChallengeExpiredException).
    /// </summary>
    [Fact]
    public async Task Verify_with_expired_verification_throws()
    {
        GivenCheckReturns(PhoneVerificationResult.Expired);

        var act = async () => await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OtpChallengeExpiredException>();
        await _refreshTokens.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers resilience: a failing event publisher must not fail a login whose session is
    /// already committed.
    /// </summary>
    [Fact]
    public async Task Verify_succeeds_even_when_event_publishing_fails()
    {
        GivenCheckReturns(PhoneVerificationResult.Approved);
        var existing = User.CreateForPhone(Guid.NewGuid(), Phone, Now);
        _users.FindByPhoneNumberAsync(Phone, Arg.Any<CancellationToken>()).Returns(existing);
        _publisher.PublishAsync(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("sqs down"));

        var response = await CreateSut()
            .VerifyAsync(Phone, Code, deviceId: null, TestContext.Current.CancellationToken);

        response.UserId.Should().Be(existing.Id);
    }
}
