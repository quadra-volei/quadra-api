using Quadra.Shared.Contracts;

namespace Quadra.Infrastructure.Storage;

/// <summary>
/// Photo part of account export and deletion (SEC-14): every upload of a person (profile photo,
/// posts, match covers, card background, ads) lives under <c>profiles/{userId}/</c> in the bucket.
/// Export lists the keys; deletion removes the whole folder. The modules clear their own keys that
/// point there (match covers and paid ads that outlive the account), so nothing links to a gone file.
/// </summary>
public sealed class PhotosPersonalData : IPersonalDataStore
{
    private readonly IProfilePhotoStorage _storage;

    public PhotosPersonalData(IProfilePhotoStorage storage)
    {
        _storage = storage;
    }

    public string Section => "photos";

    public static string FolderOf(Guid userId) => $"profiles/{userId:D}/";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) =>
        new { Keys = await _storage.ListAsync(FolderOf(userId), cancellationToken) };

    public Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        _storage.DeleteAllAsync(FolderOf(userId), cancellationToken);
}
