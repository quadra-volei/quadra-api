namespace Quadra.Modules.Auth.Entities;

/// <summary>
/// Local user record for the Auth module. Mirrors the Cognito identity through <see cref="CognitoSub"/>
/// and is the durable identifier consumed by every other module via the <c>UserRegistered</c> event.
/// </summary>
public sealed class User
{
    private User()
    {
        // EF Core materialization.
        CognitoSub = null!;
    }

    private User(
        Guid id,
        string cognitoSub,
        IdentityProvider provider,
        string? phoneNumber,
        string? email,
        UserConfirmationStatus confirmationStatus,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        CognitoSub = cognitoSub;
        Provider = provider;
        PhoneNumber = phoneNumber;
        Email = email;
        ConfirmationStatus = confirmationStatus;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public string CognitoSub { get; private set; }

    public IdentityProvider Provider { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public UserConfirmationStatus ConfirmationStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a phone-signup user. Starts as <see cref="UserConfirmationStatus.Unconfirmed"/>;
    /// the FA.3 SMS OTP flow flips it to <see cref="UserConfirmationStatus.Confirmed"/>.
    /// </summary>
    public static User CreateForPhone(
        Guid id,
        string cognitoSub,
        string phoneNumber,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cognitoSub);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        return new User(
            id,
            cognitoSub,
            IdentityProvider.Phone,
            phoneNumber,
            email: null,
            UserConfirmationStatus.Unconfirmed,
            now,
            now);
    }

    /// <summary>
    /// Creates a Google or Apple signup user. Always <see cref="UserConfirmationStatus.Confirmed"/>
    /// because the caller has already validated the external ID token.
    /// </summary>
    public static User CreateForExternal(
        Guid id,
        string cognitoSub,
        IdentityProvider provider,
        string? email,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cognitoSub);
        if (provider is not (IdentityProvider.Google or IdentityProvider.Apple))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "CreateForExternal only accepts Google or Apple.");
        }

        return new User(
            id,
            cognitoSub,
            provider,
            phoneNumber: null,
            email,
            UserConfirmationStatus.Confirmed,
            now,
            now);
    }

    /// <summary>
    /// Transitions an <see cref="UserConfirmationStatus.Unconfirmed"/> phone-signup user to
    /// <see cref="UserConfirmationStatus.Confirmed"/> on the first successful SMS OTP verify
    /// (FA.3) and bumps <see cref="UpdatedAt"/>. Idempotent: a no-op once already confirmed.
    /// </summary>
    public void Confirm(DateTimeOffset now)
    {
        if (ConfirmationStatus == UserConfirmationStatus.Confirmed)
        {
            return;
        }

        ConfirmationStatus = UserConfirmationStatus.Confirmed;
        UpdatedAt = now;
    }
}
