using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Quadra.Infrastructure.Storage;

/// <summary>
/// S3-backed implementation of <see cref="IProfilePhotoStorage"/>. Generates presigned PUT/GET URLs
/// so the API never handles the photo bytes. TTLs come from <see cref="ProfilePhotoStorageOptions"/>.
/// </summary>
public sealed class S3ProfilePhotoStorage : IProfilePhotoStorage
{
    private readonly IAmazonS3 _s3;
    private readonly ProfilePhotoStorageOptions _options;
    private readonly TimeProvider _timeProvider;

    public S3ProfilePhotoStorage(
        IAmazonS3 s3,
        IOptions<ProfilePhotoStorageOptions> options,
        TimeProvider timeProvider)
    {
        _s3 = s3;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<PhotoUploadTarget> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        var expiresAt = _timeProvider.GetUtcNow().AddMinutes(_options.PhotoUploadUrlTtlMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.ProfilePhotosBucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = expiresAt.UtcDateTime,
        };

        var url = await _s3.GetPreSignedURLAsync(request);
        return new PhotoUploadTarget(url, expiresAt);
    }

    /// <inheritdoc/>
    public async Task<string> GetReadUrlAsync(string objectKey, CancellationToken cancellationToken)
    {
        var expiresAt = _timeProvider.GetUtcNow().AddMinutes(_options.PhotoReadUrlTtlMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.ProfilePhotosBucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
        };

        return await _s3.GetPreSignedURLAsync(request);
    }
}
