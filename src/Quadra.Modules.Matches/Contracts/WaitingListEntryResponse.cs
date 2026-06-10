namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Read model representing a single waiting list entry.
/// </summary>
public sealed record WaitingListEntryResponse(
    Guid PlayerId,
    string PlayerType,
    int Position,
    DateTimeOffset CreatedAt);
