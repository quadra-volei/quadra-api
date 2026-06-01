namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// 200 OK body for the SMS OTP "initiate" step. <see cref="Session"/> is the opaque Cognito
/// challenge session the client must echo back on the "verify" step. <see cref="Delivery"/>
/// reuses the FA.2 <see cref="SmsDeliveryDetails"/> shape (masked destination).
/// </summary>
public sealed record SmsOtpInitiateResponse(
    string Session,
    SmsDeliveryDetails Delivery);
