namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// 200 OK body for the SMS OTP "initiate" step. The pending verification is keyed by phone
/// number at the SMS provider, so the client only echoes the phone number back on "verify".
/// </summary>
public sealed record SmsOtpInitiateResponse(SmsDeliveryDetails Delivery);

/// <summary>
/// Where the code was sent. <see cref="DeliveryDestination"/> is the masked phone number.
/// </summary>
public sealed record SmsDeliveryDetails(
    string DeliveryMedium,
    string DeliveryDestination);
