namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Body of <c>201 Created</c> on a successful signup.
/// </summary>
public sealed record SignupResponse(
    Guid UserId,
    string CognitoSub,
    string Provider,
    string ConfirmationStatus,
    SmsDeliveryDetails? SmsDelivery);

/// <summary>
/// SMS dispatch details returned only when <see cref="SignupResponse.Provider"/> is "phone".
/// </summary>
public sealed record SmsDeliveryDetails(
    string DeliveryMedium,
    string DeliveryDestination);
