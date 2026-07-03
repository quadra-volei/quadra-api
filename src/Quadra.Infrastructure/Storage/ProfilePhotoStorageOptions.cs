namespace Quadra.Infrastructure.Storage;

/// <summary>
/// Configuration for <see cref="S3ProfilePhotoStorage"/>, bound from the <c>Aws:S3</c> section.
/// </summary>
public sealed class ProfilePhotoStorageOptions
{
    public const string SectionName = "Aws:S3";

    /// <summary>Name of the S3 bucket holding profile photos.</summary>
    public string ProfilePhotosBucket { get; set; } = string.Empty;

    /// <summary>Time-to-live, in minutes, of a presigned upload (PUT) URL.</summary>
    public int PhotoUploadUrlTtlMinutes { get; set; } = 15;

    /// <summary>Time-to-live, in minutes, of a presigned read (GET) URL.</summary>
    public int PhotoReadUrlTtlMinutes { get; set; } = 60;
}
