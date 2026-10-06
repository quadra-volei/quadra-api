namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/matches/{id}/guests</c>.
/// </summary>
/// <param name="Name">Required, 1–80 characters after trimming.</param>
/// <param name="Position">Optional court position: <c>LEV | PON | OPO | CEN | LIB | COR</c>.</param>
public sealed record AddGuestRequest(string Name, string? Position);
