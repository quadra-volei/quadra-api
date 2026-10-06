namespace Quadra.Infrastructure.Storage;

/// <summary>
/// <see cref="IProfilePhotoStorage"/> used when <c>Aws:S3:ProfilePhotosBucket</c> is empty.
/// Everything except photos keeps working: profiles are served without a photo URL and an upload
/// request is refused with <see cref="ProfilePhotoStorageUnavailableException"/>.
/// </summary>
public sealed class UnconfiguredProfilePhotoStorage : IProfilePhotoStorage
{
    public Task<PhotoUploadTarget> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        throw new ProfilePhotoStorageUnavailableException();
    }

    public Task<string?> GetReadUrlAsync(string objectKey, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}
