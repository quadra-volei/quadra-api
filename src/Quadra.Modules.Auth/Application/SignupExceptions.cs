namespace Quadra.Modules.Auth.Application;

/// <summary>
/// The phone number is already linked to a local <c>users</c> row. Maps to HTTP 409.
/// </summary>
public sealed class PhoneAlreadyRegisteredException : Exception
{
    public PhoneAlreadyRegisteredException(string phoneNumber)
        : base($"A user with the supplied phone number is already registered.")
    {
        PhoneNumber = phoneNumber;
    }

    public string PhoneNumber { get; }
}

/// <summary>
/// The external Google/Apple identity is already linked to a Cognito user. Maps to HTTP 409.
/// </summary>
public sealed class ExternalIdentityAlreadyLinkedException : Exception
{
    public ExternalIdentityAlreadyLinkedException(string provider, string externalSub)
        : base("The external identity is already linked to an existing account.")
    {
        Provider = provider;
        ExternalSub = externalSub;
    }

    public string Provider { get; }

    public string ExternalSub { get; }
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
/// Cognito rejected the request for reasons attributable to the client (e.g. policy violation,
/// SMS could not be initiated). Maps to HTTP 422.
/// </summary>
public sealed class CognitoPolicyViolationException : Exception
{
    public CognitoPolicyViolationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Catch-all for unexpected Cognito service errors. Maps to HTTP 502.
/// </summary>
public sealed class CognitoUnavailableException : Exception
{
    public CognitoUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
