namespace Quadra.Modules.Geo.Places;

/// <summary>
/// Address / venue search behind the "LOCAL" field of the create-match form. The provider
/// (Google Places, OpenStreetMap) is chosen by configuration — see <see cref="PlacesOptions"/> —
/// and its key never leaves the backend.
/// </summary>
public interface IPlaceSearchService
{
    /// <summary>Suggestions for what the user typed so far, best match first.</summary>
    /// <exception cref="PlaceSearchUnavailableException">The provider failed or timed out.</exception>
    Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(PlaceSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a suggestion that came without coordinates. Null when the provider does not
    /// know the id.
    /// </summary>
    /// <exception cref="PlaceSearchUnavailableException">The provider failed or timed out.</exception>
    Task<PlaceDetails?> GetAsync(string placeId, string? sessionToken, CancellationToken cancellationToken);
}

/// <param name="Text">What the user typed.</param>
/// <param name="Latitude">Where the user is, to rank closer places first. Optional.</param>
/// <param name="Longitude">See <paramref name="Latitude"/>.</param>
/// <param name="SessionToken">
/// Opaque id the client keeps for one typing session (Google bills a session — the keystrokes
/// plus the final details call — as one request).
/// </param>
public sealed record PlaceSearchQuery(string Text, double? Latitude, double? Longitude, string? SessionToken);

/// <summary>
/// One suggestion. When <paramref name="Latitude"/>/<paramref name="Longitude"/> are null the
/// client asks for <c>GET /places/{id}</c> after the user picks it.
/// </summary>
public sealed record PlaceSuggestion(string Id, string Title, string Subtitle, double? Latitude, double? Longitude);

public sealed record PlaceDetails(string Id, string Title, string Address, double Latitude, double Longitude);

/// <summary>The place provider could not be reached or answered with an error.</summary>
public sealed class PlaceSearchUnavailableException : Exception
{
    public PlaceSearchUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
