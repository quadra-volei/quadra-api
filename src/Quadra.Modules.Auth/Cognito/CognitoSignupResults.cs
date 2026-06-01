namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// Outcome of a Cognito phone signup. <see cref="UserSub"/> is the durable Cognito identifier
/// that becomes <c>users.cognito_sub</c>. <see cref="DeliveryMedium"/> / <see cref="DeliveryDestination"/>
/// describe the SMS Cognito just dispatched (masked phone).
/// </summary>
public sealed record PhoneSignupResult(
    string UserSub,
    string DeliveryMedium,
    string DeliveryDestination);

/// <summary>
/// Outcome of provisioning a Cognito user for a federated Google/Apple identity.
/// </summary>
public sealed record ExternalSignupResult(string UserSub);
