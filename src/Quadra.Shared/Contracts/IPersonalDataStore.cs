namespace Quadra.Shared.Contracts;

/// <summary>
/// Implemented by every module that stores data about a person. Account export and deletion
/// (LGPD access and erasure, audit SEC-14) call every implementation: export gathers each
/// <see cref="Section"/>, deletion erases each module's rows before the account itself goes.
/// </summary>
public interface IPersonalDataStore
{
    /// <summary>Key of this module's part of the export (e.g. <c>profile</c>).</summary>
    string Section { get; }

    /// <summary>What the module keeps about <paramref name="userId"/>, ready to serialize.</summary>
    Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Erases (or detaches) the person's data. Idempotent: a retry after a failure is safe.</summary>
    Task EraseAsync(Guid userId, CancellationToken cancellationToken);
}
