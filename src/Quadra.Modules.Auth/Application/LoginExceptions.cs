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
/// The OTP challenge session was invalid or expired. Maps to HTTP 401.
/// </summary>
public sealed class OtpChallengeExpiredException : Exception
{
    public OtpChallengeExpiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The credential validated upstream but no local <c>users</c> row / linked Cognito user exists.
/// The user must complete FA.2 signup first. Maps to HTTP 404.
/// </summary>
public sealed class UserNotFoundForLoginException : Exception
{
    public UserNotFoundForLoginException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The refresh token is unknown locally, revoked, expired, or rejected by Cognito. Maps to HTTP 401.
/// </summary>
public sealed class RefreshTokenRejectedException : Exception
{
    public RefreshTokenRejectedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Cognito throttled the request (TooManyRequests / LimitExceeded). Maps to HTTP 429.
/// </summary>
public sealed class CognitoAuthThrottledException : Exception
{
    public CognitoAuthThrottledException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
