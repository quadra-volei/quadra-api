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
    private readonly IImageModerator _moderator;

    public S3ProfilePhotoStorage(
        IAmazonS3 s3,
        IOptions<ProfilePhotoStorageOptions> options,
        TimeProvider timeProvider,
        IImageModerator? moderator = null)
    {
        _s3 = s3;
        _options = options.Value;
        _timeProvider = timeProvider;
        _moderator = moderator ?? new NoImageModerator();
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
    public async Task<bool> IsWithinLimitsAsync(string objectKey, CancellationToken cancellationToken)
    {
        GetObjectMetadataResponse metadata;
        try
        {
            metadata = await _s3.GetObjectMetadataAsync(_options.ProfilePhotosBucket, objectKey, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        // Small enough, and (when a moderation provider is on) no adult content (DECISIONS #76).
        if (metadata.ContentLength <= _options.MaxPhotoBytes
            && await _moderator.IsAllowedAsync((await GetReadUrlAsync(objectKey, cancellationToken))!, cancellationToken))
        {
            return true;
        }

        await _s3.DeleteObjectAsync(_options.ProfilePhotosBucket, objectKey, cancellationToken);
        return false;
    }

    /// <inheritdoc/>
    public async Task<string?> GetReadUrlAsync(string objectKey, CancellationToken cancellationToken)
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken)
    {
        EnsurePersonPrefix(prefix);
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _options.ProfilePhotosBucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await _s3.ListObjectsV2Async(request, cancellationToken);
            keys.AddRange((response.S3Objects ?? []).Select(o => o.Key));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return keys;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAllAsync(string prefix, CancellationToken cancellationToken)
    {
        var keys = await ListAsync(prefix, cancellationToken);

        // DeleteObjects takes at most 1000 keys per call.
        foreach (var chunk in keys.Chunk(1000))
        {
            await _s3.DeleteObjectsAsync(
                new DeleteObjectsRequest
                {
                    BucketName = _options.ProfilePhotosBucket,
                    Objects = chunk.Select(k => new KeyVersion { Key = k }).ToList(),
                    Quiet = true,
                },
                cancellationToken);
        }

        return keys.Count;
    }

    // A prefix like "" or "profiles/" would reach other people's photos: only one person's folder.
    private static void EnsurePersonPrefix(string prefix)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, "^profiles/[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}/$"))
        {
            throw new ArgumentException("Only one person's photo folder (profiles/{userId}/) may be listed or deleted.", nameof(prefix));
        }
    }
}
