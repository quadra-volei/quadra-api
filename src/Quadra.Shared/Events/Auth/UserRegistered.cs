namespace Quadra.Shared.Events.Auth;

/// <summary>
/// Integration event published by the Auth module after a new local <c>users</c> row is committed
/// (the first successful login of a phone number or Google/Apple account).
/// Downstream modules (Profile, etc.) consume this to bootstrap their own per-user records.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record UserRegistered(
    Guid UserId,
    string Provider,
    string? PhoneNumber,
    string? Email,
    DateTimeOffset OccurredAt);
