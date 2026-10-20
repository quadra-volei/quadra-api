namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/profiles/me</c> — used both to complete onboarding (frontend
/// S4) and to edit the profile later (S10).
/// </summary>
/// <param name="FirstName">Required, 1–40 characters after trimming.</param>
/// <param name="LastName">Required, 1–40 characters after trimming.</param>
/// <param name="Handle">Required unique <c>@</c> identifier: 3–20 of a–z, 0–9, underscore. Case-insensitive; stored lowercase.</param>
/// <param name="BirthDate">Required, a past date (<c>yyyy-MM-dd</c>).</param>
/// <param name="Position">Required: <c>LEV | PON | OPO | CEN | LIB | COR</c>.</param>
/// <param name="Modality"><c>Indoor | Beach</c>. Required to complete onboarding; afterwards null keeps the current value.</param>
/// <param name="Level">Self-declared <c>Beginner | Intermediate | Advanced</c>. Required to complete onboarding and only accepted then.</param>
/// <param name="PhotoObjectKey">Object key returned by the photo upload-url endpoint, or null for no photo.</param>
public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string Handle,
    DateOnly BirthDate,
    string Position,
    string? Modality,
    string? Level,
    string? PhotoObjectKey);
