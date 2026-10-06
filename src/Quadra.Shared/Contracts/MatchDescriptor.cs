namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module projection of a match's descriptive fields (name and scheduled time),
/// exposed by the Matches module through <see cref="IMatchDescriptorReader"/> and consumed by the
/// Background Worker when enriching a finished-match participation snapshot (F2.1).
/// </summary>
public sealed record MatchDescriptor(
    Guid MatchId,
    string Name,
    DateTimeOffset DateTime);
