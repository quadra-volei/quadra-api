namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Response for <c>GET /api/v1/admin/arenas/candidates</c> (DECISIONS #94): the Google places in a
/// rectangle nobody has decided on yet, the ones with games nearby first, and the approved arenas
/// there (to say "it is the same arena as…"). Names come from <c>GET /places/{placeId}</c>.
/// </summary>
public sealed record CurationCandidatesResponse(
    IReadOnlyList<CurationCandidateResponse> Candidates,
    IReadOnlyList<CurationArenaResponse> Arenas,
    int PendingCount,          // in the whole rectangle
    bool Truncated);           // more pending than listed

public sealed record CurationCandidateResponse(
    string PlaceId,
    double Latitude,
    double Longitude,
    string Kind,                       // which of our searches found it: Court | Sand | Park
    double DistanceKm,                 // from lat/lon, or the rectangle's centre
    int GamesNearby,                   // upcoming public games within 100 m
    IReadOnlyList<string> Neighbors,   // other pending places within 100 m: likely the same place
    DateTimeOffset ExpiresAt);         // its Google coordinates go then (30-day limit) unless searched again

public sealed record CurationArenaResponse(Guid Id, string? PlaceId, double Latitude, double Longitude, string Kind);

/// <summary>
/// Body of <c>POST /api/v1/admin/arenas/decisions</c>. <see cref="Action"/>: <c>Approve</c> (a new
/// arena at <see cref="Latitude"/>/<see cref="Longitude"/> of <see cref="Kind"/>; <see cref="PinMoved"/>
/// when the team moved the pin), <c>Reject</c> (not a place to play, never offered again) or <c>Link</c>
/// (an entrance or duplicate of the active arena <see cref="ArenaId"/>).
/// </summary>
public sealed record CurationDecisionRequest(
    string PlaceId,
    string Action,
    string? Kind = null,
    double? Latitude = null,
    double? Longitude = null,
    bool PinMoved = false,
    Guid? ArenaId = null);

public sealed record CurationDecisionResponse(Guid? ArenaId);

/// <summary>Body of <c>POST /api/v1/admin/arenas/discover</c>: search Google around a point.</summary>
public sealed record DiscoverCandidatesRequest(double Lat, double Lon, double RadiusKm);

/// <summary>How many grid cells were searched: 0 when the area is fresh, or Google is not configured.</summary>
public sealed record DiscoverCandidatesResponse(int CellsSearched);
