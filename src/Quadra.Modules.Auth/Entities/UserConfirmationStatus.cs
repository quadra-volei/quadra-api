namespace Quadra.Modules.Auth.Entities;

/// <summary>
/// Confirmation status for a local <see cref="User"/>. Persisted as string ("Unconfirmed" | "Confirmed").
/// Phone signups start <see cref="Unconfirmed"/> and become <see cref="Confirmed"/> only after the
/// FA.3 SMS OTP verification. Google/Apple signups are <see cref="Confirmed"/> from creation because
/// the ID token is validated server-side.
/// </summary>
public enum UserConfirmationStatus
{
    Unconfirmed = 0,
    Confirmed = 1,
}
