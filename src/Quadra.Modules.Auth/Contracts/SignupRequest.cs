namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/auth/signup</c>. <see cref="Provider"/> is the discriminator
/// selecting which nested payload must be populated.
/// </summary>
public sealed record SignupRequest(
    string Provider,
    PhoneSignupPayload? Phone,
    OidcSignupPayload? Google,
    OidcSignupPayload? Apple);

/// <summary>
/// Phone-signup payload. <see cref="PhoneNumber"/> must be E.164.
/// </summary>
public sealed record PhoneSignupPayload(string PhoneNumber);

/// <summary>
/// Google/Apple signup payload. <see cref="IdToken"/> is the raw JWT obtained by the
/// mobile client from the provider's native SDK.
/// </summary>
public sealed record OidcSignupPayload(string IdToken);
