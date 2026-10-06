namespace Quadra.Infrastructure.Storage;

/// <summary>A presigned upload target for a profile photo.</summary>
public sealed record PhotoUploadTarget(string UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Abstraction over profile-photo object storage (any S3-compatible service). The API is never in
/// the binary path: clients upload directly to the bucket via a presigned PUT URL and read via a
/// short-lived GET URL. Photos are optional — when no bucket is configured,
/// <see cref="UnconfiguredProfilePhotoStorage"/> is used and profiles simply have no photo.
/// </summary>
public interface IProfilePhotoStorage
{
    /// <summary>
    /// Creates a short-lived presigned PUT URL the client uses to upload the photo bytes for
    /// <paramref name="objectKey"/> with the given <paramref name="contentType"/>. Throws
    /// <see cref="ProfilePhotoStorageUnavailableException"/> when no storage is configured.
    /// </summary>
    Task<PhotoUploadTarget> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a short-lived presigned GET URL to read the object at <paramref name="objectKey"/>,
    /// or returns <c>null</c> when no storage is configured.
    /// </summary>
    Task<string?> GetReadUrlAsync(string objectKey, CancellationToken cancellationToken);
}

/// <summary>
/// Photo uploads were requested but no object storage is configured. Maps to HTTP 503.
/// </summary>
public sealed class ProfilePhotoStorageUnavailableException : Exception
{
    public ProfilePhotoStorageUnavailableException()
        : base("Profile photo storage is not configured.")
    {
    }
}
