namespace Quadra.Infrastructure.Storage;

/// <summary>
/// Configuration for profile-photo storage, bound from the <c>Aws:S3</c> section. Leave
/// <see cref="ProfilePhotosBucket"/> empty to run without photos. To use an S3-compatible
/// service other than AWS (Cloudflare R2, Backblaze B2, MinIO…), set <see cref="ServiceUrl"/>
/// together with <see cref="AccessKeyId"/> and <see cref="SecretAccessKey"/>.
/// </summary>
public sealed class ProfilePhotoStorageOptions
{
    public const string SectionName = "Aws:S3";

    private const int DefaultUploadUrlTtlMinutes = 15;
    private const int DefaultReadUrlTtlMinutes = 60;

    private int _photoUploadUrlTtlMinutes = DefaultUploadUrlTtlMinutes;
    private int _photoReadUrlTtlMinutes = DefaultReadUrlTtlMinutes;

    /// <summary>Bucket that stores profile photos. Empty = photo storage disabled.</summary>
    public string ProfilePhotosBucket { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint of an S3-compatible service (e.g. <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>).
    /// Empty = AWS S3, with region and credentials taken from the standard AWS environment.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Signing region for <see cref="ServiceUrl"/>. Cloudflare R2 uses <c>auto</c>.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>Access key for <see cref="ServiceUrl"/>. A secret — never commit it.</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Secret key for <see cref="ServiceUrl"/>. A secret — never commit it.</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Lifetime of a presigned upload URL. Non-positive values fall back to 15.</summary>
    public int PhotoUploadUrlTtlMinutes
    {
        get => _photoUploadUrlTtlMinutes;
        set => _photoUploadUrlTtlMinutes = value > 0 ? value : DefaultUploadUrlTtlMinutes;
    }

    /// <summary>Lifetime of a presigned read URL. Non-positive values fall back to 60.</summary>
    public int PhotoReadUrlTtlMinutes
    {
        get => _photoReadUrlTtlMinutes;
        set => _photoReadUrlTtlMinutes = value > 0 ? value : DefaultReadUrlTtlMinutes;
    }

    /// <summary>True when a bucket is configured, i.e. photos are enabled.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProfilePhotosBucket);

    /// <summary>True when an S3-compatible endpoint (not AWS itself) is configured.</summary>
    public bool UsesCustomEndpoint => !string.IsNullOrWhiteSpace(ServiceUrl);
}
