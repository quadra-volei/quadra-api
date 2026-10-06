namespace Quadra.Modules.Auth.Entities;

/// <summary>
/// A persisted handle to a refresh token issued by this API. Only the SHA-256 hash (hex) of the
/// raw token is stored in <see cref="TokenHash"/> — the raw token is returned to the client once
/// and never persisted, so a DB leak cannot replay sessions. A non-null <see cref="RevokedAt"/>
/// or an elapsed <see cref="ExpiresAt"/> renders the row unusable.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
        // EF Core materialization.
        TokenHash = null!;
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        string? deviceId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        DeviceId = deviceId;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        RevokedAt = null;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public string? DeviceId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// Issues a new refresh-token row. <paramref name="tokenHash"/> is the SHA-256 hex of the raw
    /// refresh token computed by the caller.
    /// </summary>
    public static RefreshToken Issue(
        Guid id,
        Guid userId,
        string tokenHash,
        string? deviceId,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return new RefreshToken(id, userId, tokenHash, deviceId, issuedAt, expiresAt);
    }

    /// <summary>
    /// Marks the token as revoked (logout). Idempotent: keeps the first revocation timestamp.
    /// </summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }

    /// <summary>
    /// True when the token can still be used to refresh a session.
    /// </summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
