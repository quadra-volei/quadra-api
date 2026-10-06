namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/profiles/me</c>. Only editable fields are present — stats and
/// level are derived and never client-writable.
/// </summary>
public sealed record UpdateProfileRequest(
    string DisplayName,
    string? PrimaryPosition,
    string? SecondaryPosition,
    string? PhotoObjectKey);
