using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// Internal abstraction over the Cognito Identity Provider SDK used by the signup flow.
/// Keeps the SDK contained to the Auth module per the module-boundary rule.
/// </summary>
public interface ICognitoSignupClient
{
    /// <summary>
    /// Provisions a new Cognito user via <c>SignUpAsync</c> using the supplied E.164 phone as username.
    /// Cognito dispatches an SMS OTP; verification happens later in FA.3.
    /// </summary>
    Task<PhoneSignupResult> SignUpPhoneAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Provisions a Cognito user backed by an external Google/Apple identity using
    /// <c>AdminCreateUserAsync</c> with <c>MessageAction = SUPPRESS</c>.
    /// </summary>
    Task<ExternalSignupResult> ProvisionExternalUserAsync(
        IdentityProvider provider,
        string externalSub,
        string? email,
        bool emailVerified,
        CancellationToken cancellationToken);

    /// <summary>
    /// Links a Cognito destination user to the external IdP identity via
    /// <c>AdminLinkProviderForUserAsync</c>.
    /// </summary>
    Task LinkExternalIdentityAsync(
        IdentityProvider provider,
        string destinationUsername,
        string externalSub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the Cognito <c>sub</c> for an existing external identity, or <c>null</c> if none exists.
    /// Used as the dedup pre-check before <see cref="ProvisionExternalUserAsync"/>.
    /// </summary>
    Task<string?> FindExternalUserSubAsync(
        IdentityProvider provider,
        string externalSub,
        CancellationToken cancellationToken);
}
