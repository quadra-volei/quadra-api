namespace Quadra.Modules.Geo.Places;

/// <summary>
/// <c>Places</c> configuration section: which service answers the address search.
///
///  - <c>Google</c> — Google Places API (New); needs <see cref="GoogleApiKey"/>.
///  - <c>OpenStreetMap</c> — the public Photon geocoder; no key, fair-use only.
///  - <c>None</c> — search disabled (always empty); the app falls back to free text.
///
/// With <see cref="Provider"/> empty: Google when a key is set, OpenStreetMap otherwise.
/// </summary>
public sealed class PlacesOptions
{
    public const string SectionName = "Places";

    public const string Google = "Google";
    public const string OpenStreetMap = "OpenStreetMap";
    public const string None = "None";

    public string Provider { get; set; } = string.Empty;

    /// <summary>Server-side key for the Google Places API (New). Never sent to clients.</summary>
    public string GoogleApiKey { get; set; } = string.Empty;

    /// <summary>The provider in effect, or null when <see cref="Provider"/> is not a known name.</summary>
    public string? ResolvedProvider
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Provider))
            {
                return string.IsNullOrWhiteSpace(GoogleApiKey) ? OpenStreetMap : Google;
            }

            return new[] { Google, OpenStreetMap, None }
                .FirstOrDefault(known => string.Equals(known, Provider.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Known provider, and a key when that provider is Google.</summary>
    public bool IsValid =>
        ResolvedProvider is not null
        && (ResolvedProvider != Google || !string.IsNullOrWhiteSpace(GoogleApiKey));
}
