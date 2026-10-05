namespace Quadra.Modules.Auth.Entities;

/// <summary>
/// Local user record for the Auth module — the source of truth for identity. <see cref="Id"/> is
/// the <c>sub</c> of every access token and the durable identifier consumed by every other module
/// via the <c>UserRegistered</c> event. A row only exists once the identity is proven (valid SMS
/// OTP or validated Google/Apple ID token).
/// </summary>
public sealed class User
{
    private User()
    {
        // EF Core materialization.
    }

    private User(
        Guid id,
        IdentityProvider provider,
        string? externalSubject,
        string? phoneNumber,
        string? email,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Provider = provider;
        ExternalSubject = externalSubject;
        PhoneNumber = phoneNumber;
        Email = email;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; private set; }

    public IdentityProvider Provider { get; private set; }

    /// <summary>
    /// The <c>sub</c> of the Google/Apple account this user signs in with. Null for phone users.
    /// Unique per <see cref="Provider"/>.
    /// </summary>
    public string? ExternalSubject { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a user from a phone number that has just passed SMS OTP verification.
    /// </summary>
    public static User CreateForPhone(
        Guid id,
        string phoneNumber,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        return new User(
            id,
            IdentityProvider.Phone,
            externalSubject: null,
            phoneNumber,
            email: null,
            now,
            now);
    }

    /// <summary>
    /// Creates a user from a Google or Apple account whose ID token the caller has already
    /// validated. <paramref name="externalSubject"/> is that token's <c>sub</c>.
    /// </summary>
    public static User CreateForExternal(
        Guid id,
        IdentityProvider provider,
        string externalSubject,
        string? email,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSubject);
        if (provider is not (IdentityProvider.Google or IdentityProvider.Apple))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "CreateForExternal only accepts Google or Apple.");
        }

        return new User(
            id,
            provider,
            externalSubject,
            phoneNumber: null,
            email,
            now,
            now);
    }
}
