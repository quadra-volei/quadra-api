namespace Quadra.Modules.Auth.Application;

/// <summary>
/// The supplied SMS OTP code was wrong. Maps to HTTP 401.
/// </summary>
public sealed class InvalidOtpException : Exception
{
    public InvalidOtpException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// No pending OTP verification exists for the phone number (expired, already used, or never
/// started). Maps to HTTP 401.
/// </summary>
public sealed class OtpChallengeExpiredException : Exception
{
    public OtpChallengeExpiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The supplied Google/Apple ID token failed signature, issuer, audience, or expiry validation.
/// Maps to HTTP 401.
/// </summary>
public sealed class OidcTokenInvalidException : Exception
{
    public OidcTokenInvalidException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The refresh token is unknown, revoked, expired, or was already rotated. Maps to HTTP 401.
/// </summary>
public sealed class RefreshTokenRejectedException : Exception
{
    public RefreshTokenRejectedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The SMS provider refused the phone number (invalid, landline, unsupported region).
/// Maps to HTTP 422.
/// </summary>
public sealed class PhoneNumberRejectedException : Exception
{
    public PhoneNumberRejectedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The SMS provider throttled the request (too many sends or too many attempts for the number).
/// Maps to HTTP 429.
/// </summary>
public sealed class PhoneVerificationThrottledException : Exception
{
    public PhoneVerificationThrottledException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Catch-all for unexpected SMS provider errors. Maps to HTTP 502.
/// </summary>
public sealed class PhoneVerificationUnavailableException : Exception
{
    public PhoneVerificationUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
