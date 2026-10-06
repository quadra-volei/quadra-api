namespace Quadra.Infrastructure.Storage;

/// <summary>A presigned upload target for a profile photo.</summary>
public sealed record PhotoUploadTarget(string UploadUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Abstraction over profile-photo object storage (S3). The API is never in the binary path:
/// clients upload directly to S3 via a presigned PUT URL and read via a short-lived GET URL.
/// </summary>
public interface IProfilePhotoStorage
{
    /// <summary>
    /// Creates a short-lived presigned PUT URL the client uses to upload the photo bytes for
    /// <paramref name="objectKey"/> with the given <paramref name="contentType"/>.
    /// </summary>
    Task<PhotoUploadTarget> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>Creates a short-lived presigned GET URL to read the object at <paramref name="objectKey"/>.</summary>
    Task<string> GetReadUrlAsync(string objectKey, CancellationToken cancellationToken);
}
