namespace Quadra.Shared.Events.Auth;

/// <summary>
/// Integration event published by the Auth module after a successful interactive login
/// (SMS OTP verify, Google, or Apple) issues a token. It is NOT published on a plain
/// refresh-token renewal. At-least-once delivery — consumers must be idempotent.
/// Declared consumer in MVP: Quadra.Modules.Profile (last-login freshness signal, owned by F2.1).
/// </summary>
public sealed record UserLoggedIn(
    Guid UserId,
    string Provider,
    string? DeviceId,
    DateTimeOffset OccurredAt);
