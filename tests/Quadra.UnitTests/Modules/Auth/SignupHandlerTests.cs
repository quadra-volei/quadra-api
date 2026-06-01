using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// Unit tests for <see cref="SignupHandler"/>. Covers FA.2 orchestration:
/// dispatch by provider, Cognito call, DB insert, event publish AFTER commit,
/// and exception translation for the controller error mapping.
/// </summary>
public sealed class SignupHandlerTests
{
    private readonly IUserRepository _repo = Substitute.For<IUserRepository>();
    private readonly ICognitoSignupClient _cognito = Substitute.For<ICognitoSignupClient>();
    private readonly IOidcTokenValidator _oidc = Substitute.For<IOidcTokenValidator>();
    private readonly IEventPublisher _publisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(
        new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));

    private SignupHandler CreateSut()
    {
        var dbContext = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().Options);
        return new SignupHandler(
            _repo,
            dbContext,
            _cognito,
            _oidc,
            _publisher,
            _timeProvider,
            NullLogger<SignupHandler>.Instance);
    }

    // ---------------- phone branch ----------------

    /// <summary>
    /// Covers: "POST /api/v1/auth/signup triggers Cognito SignUp (phone provider)"
    /// + "Local user row is persisted (id, cognito_sub, provider, phone_number, confirmation_status, created_at)"
    /// + "Phone-signup confirmation_status starts Unconfirmed".
    /// </summary>
    [Fact]
    public async Task Phone_signup_calls_cognito_and_persists_unconfirmed_user()
    {
        const string phone = "+5511999999999";
        _repo.FindByPhoneNumberAsync(phone, Arg.Any<CancellationToken>())
            .Returns((User?)null);
        _cognito.SignUpPhoneAsync(phone, Arg.Any<CancellationToken>())
            .Returns(new PhoneSignupResult("cognito-sub-1", "SMS", "+55********99"));

        var sut = CreateSut();

        var response = await sut.HandleAsync(new SignupCommand.Phone(phone), TestContext.Current.CancellationToken);

        await _cognito.Received(1).SignUpPhoneAsync(phone, Arg.Any<CancellationToken>());

        await _repo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.Provider == IdentityProvider.Phone
                && u.PhoneNumber == phone
                && u.CognitoSub == "cognito-sub-1"
                && u.ConfirmationStatus == UserConfirmationStatus.Unconfirmed
                && u.Email == null
                && u.Id != Guid.Empty),
            Arg.Any<CancellationToken>());

        response.Provider.Should().Be("phone");
        response.ConfirmationStatus.Should().Be("Unconfirmed");
        response.UserId.Should().NotBeEmpty();
        response.CognitoSub.Should().Be("cognito-sub-1");
        response.SmsDelivery.Should().NotBeNull();
        response.SmsDelivery!.DeliveryMedium.Should().Be("SMS");
        response.SmsDelivery.DeliveryDestination.Should().Be("+55********99");
    }

    /// <summary>
    /// Covers: "409 — same phone_number already exists locally" (pre-check).
    /// </summary>
    [Fact]
    public async Task Phone_signup_when_phone_already_exists_throws_409_without_calling_cognito()
    {
        const string phone = "+5511999999999";
        var existing = User.CreateForPhone(Guid.NewGuid(), "existing-sub", phone, DateTimeOffset.UtcNow);
        _repo.FindByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns(existing);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(new SignupCommand.Phone(phone), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<PhoneAlreadyRegisteredException>();
        await _cognito.DidNotReceiveWithAnyArgs().SignUpPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserRegistered>(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "UserRegistered event is published after DB commit (not before)" — phone.
    /// </summary>
    [Fact]
    public async Task Phone_signup_publishes_user_registered_AFTER_db_commit()
    {
        const string phone = "+5511999999999";
        _repo.FindByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns((User?)null);
        _cognito.SignUpPhoneAsync(phone, Arg.Any<CancellationToken>())
            .Returns(new PhoneSignupResult("cognito-sub-1", "SMS", "+55********99"));

        var addCalled = false;
        var publishCalledBeforeAdd = false;

        _repo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                addCalled = true;
                return Task.CompletedTask;
            });

        _publisher.PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (!addCalled)
                {
                    publishCalledBeforeAdd = true;
                }

                return Task.CompletedTask;
            });

        var sut = CreateSut();

        await sut.HandleAsync(new SignupCommand.Phone(phone), TestContext.Current.CancellationToken);

        publishCalledBeforeAdd.Should().BeFalse("UserRegistered must NEVER be published before DB insert");
        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e =>
                e.Provider == "phone"
                && e.PhoneNumber == phone
                && e.ConfirmationStatus == "Unconfirmed"
                && e.Email == null
                && e.CognitoSub == "cognito-sub-1"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "422 — Cognito returns InvalidParameterException / CodeDeliveryFailureException"
    /// (the Cognito client maps these to CognitoPolicyViolationException; the handler re-throws).
    /// </summary>
    [Fact]
    public async Task Phone_signup_propagates_cognito_policy_violation()
    {
        const string phone = "+5511999999999";
        _repo.FindByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns((User?)null);
        _cognito.SignUpPhoneAsync(phone, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoPolicyViolationException("SMS delivery failed"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(new SignupCommand.Phone(phone), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CognitoPolicyViolationException>();
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserRegistered>(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "502 — unexpected Cognito service error" (propagated from cognito client).
    /// </summary>
    [Fact]
    public async Task Phone_signup_propagates_cognito_unavailable()
    {
        const string phone = "+5511999999999";
        _repo.FindByPhoneNumberAsync(phone, Arg.Any<CancellationToken>()).Returns((User?)null);
        _cognito.SignUpPhoneAsync(phone, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoUnavailableException("boom"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(new SignupCommand.Phone(phone), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<CognitoUnavailableException>();
    }

    // ---------------- google branch ----------------

    /// <summary>
    /// Covers: "POST /signup triggers AdminCreateUser/AdminLinkProviderForUser (Google)"
    /// + "Google/Apple start Confirmed"
    /// + Local user row persisted with id, cognito_sub, provider, email, confirmation_status, created_at.
    /// </summary>
    [Fact]
    public async Task Google_signup_validates_token_then_provisions_and_links_user_as_confirmed()
    {
        const string idToken = "eyJ.google.token";
        const string externalSub = "google-sub-1";
        const string email = "USER@example.COM";

        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(externalSub, email, EmailVerified: true));
        _cognito.FindExternalUserSubAsync(IdentityProvider.Google, externalSub, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _cognito.ProvisionExternalUserAsync(
            IdentityProvider.Google,
            externalSub,
            "user@example.com",
            true,
            Arg.Any<CancellationToken>())
            .Returns(new ExternalSignupResult("cognito-sub-google"));

        var sut = CreateSut();

        var response = await sut.HandleAsync(
            new SignupCommand.External(IdentityProvider.Google, idToken),
            TestContext.Current.CancellationToken);

        await _oidc.Received(1).ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>());
        await _cognito.Received(1).ProvisionExternalUserAsync(
            IdentityProvider.Google, externalSub, "user@example.com", true, Arg.Any<CancellationToken>());
        await _cognito.Received(1).LinkExternalIdentityAsync(
            IdentityProvider.Google, "cognito-sub-google", externalSub, Arg.Any<CancellationToken>());

        await _repo.Received(1).AddAsync(
            Arg.Is<User>(u =>
                u.Provider == IdentityProvider.Google
                && u.CognitoSub == "cognito-sub-google"
                && u.Email == "user@example.com"
                && u.PhoneNumber == null
                && u.ConfirmationStatus == UserConfirmationStatus.Confirmed
                && u.Id != Guid.Empty),
            Arg.Any<CancellationToken>());

        response.Provider.Should().Be("google");
        response.ConfirmationStatus.Should().Be("Confirmed");
        response.CognitoSub.Should().Be("cognito-sub-google");
        response.SmsDelivery.Should().BeNull();
    }

    /// <summary>
    /// Covers: "401 — supplied Google/Apple ID token fails signature/issuer/audience/expiry validation".
    /// </summary>
    [Fact]
    public async Task Google_signup_with_invalid_token_throws_401_without_calling_cognito()
    {
        const string idToken = "eyJ.bad.token";
        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("signature failed"));

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            new SignupCommand.External(IdentityProvider.Google, idToken),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OidcTokenInvalidException>();
        await _cognito.DidNotReceiveWithAnyArgs().ProvisionExternalUserAsync(
            Arg.Any<IdentityProvider>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserRegistered>(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "409 — external identity already linked in Cognito (AdminListUsers pre-check)".
    /// </summary>
    [Fact]
    public async Task Google_signup_when_external_identity_already_linked_throws_409()
    {
        const string idToken = "eyJ.google.token";
        const string externalSub = "google-sub-1";

        _oidc.ValidateAsync(idToken, OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(externalSub, "user@example.com", true));
        _cognito.FindExternalUserSubAsync(IdentityProvider.Google, externalSub, Arg.Any<CancellationToken>())
            .Returns("existing-cognito-sub");

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(
            new SignupCommand.External(IdentityProvider.Google, idToken),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ExternalIdentityAlreadyLinkedException>();
        await _cognito.DidNotReceiveWithAnyArgs().ProvisionExternalUserAsync(
            Arg.Any<IdentityProvider>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "UserRegistered event published after DB commit" — external branch + Apple support.
    /// </summary>
    [Fact]
    public async Task Apple_signup_publishes_user_registered_AFTER_db_commit_with_confirmed_status()
    {
        const string idToken = "eyJ.apple.token";
        const string externalSub = "apple-sub-1";

        _oidc.ValidateAsync(idToken, OidcProvider.Apple, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims(externalSub, "private-relay@privaterelay.appleid.com", true));
        _cognito.FindExternalUserSubAsync(IdentityProvider.Apple, externalSub, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _cognito.ProvisionExternalUserAsync(
            IdentityProvider.Apple,
            externalSub,
            "private-relay@privaterelay.appleid.com",
            true,
            Arg.Any<CancellationToken>())
            .Returns(new ExternalSignupResult("cognito-sub-apple"));

        var addCalled = false;
        var publishedBeforeAdd = false;

        _repo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                addCalled = true;
                return Task.CompletedTask;
            });

        _publisher.PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (!addCalled)
                {
                    publishedBeforeAdd = true;
                }

                return Task.CompletedTask;
            });

        var sut = CreateSut();

        var response = await sut.HandleAsync(
            new SignupCommand.External(IdentityProvider.Apple, idToken),
            TestContext.Current.CancellationToken);

        publishedBeforeAdd.Should().BeFalse("event must be published only after DB commit");
        response.Provider.Should().Be("apple");
        response.ConfirmationStatus.Should().Be("Confirmed");

        await _publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e =>
                e.Provider == "apple"
                && e.ConfirmationStatus == "Confirmed"
                && e.Email == "private-relay@privaterelay.appleid.com"
                && e.PhoneNumber == null
                && e.CognitoSub == "cognito-sub-apple"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Helper fixed TimeProvider for deterministic OccurredAt assertions.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
