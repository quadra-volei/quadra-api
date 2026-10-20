namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// 200 OK body for <c>GET /api/v1/profiles/handle-availability</c>. <see cref="Handle"/> is the
/// normalized (lowercase) handle that was checked.
/// </summary>
public sealed record HandleAvailabilityResponse(string Handle, bool Available);
