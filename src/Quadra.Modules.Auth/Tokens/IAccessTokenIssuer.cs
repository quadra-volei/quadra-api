using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Tokens;

/// <summary>
/// A signed access token and its lifetime in seconds.
/// </summary>
public sealed record AccessToken(string Value, int ExpiresIn);

/// <summary>
/// Issues the API's own signed access tokens (JWT). The <c>sub</c> claim is the local
/// <c>users.id</c>, which every other module reads as the caller's identity.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, DateTimeOffset now);
}
