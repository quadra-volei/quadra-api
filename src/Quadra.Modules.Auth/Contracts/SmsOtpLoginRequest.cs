namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/auth/login/sms-otp</c>. A single endpoint discriminated by
/// <see cref="Step"/> ("initiate" | "verify"). On "initiate" only <see cref="PhoneNumber"/> is used
/// (calling it again resends the code); on "verify" <see cref="Code"/> becomes required.
/// </summary>
public sealed record SmsOtpLoginRequest(
    string Step,
    string PhoneNumber,
    string? Code,
    string? DeviceId);
