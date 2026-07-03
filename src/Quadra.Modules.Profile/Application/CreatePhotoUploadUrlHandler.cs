using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Contracts;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Issues a short-lived presigned S3 PUT URL so the client uploads the photo bytes directly to S3.
/// The reference is persisted later via <c>PUT /profiles/me</c>.
/// </summary>
public sealed class CreatePhotoUploadUrlHandler
{
    private readonly IProfilePhotoStorage _photoStorage;

    public CreatePhotoUploadUrlHandler(IProfilePhotoStorage photoStorage)
    {
        _photoStorage = photoStorage;
    }

    public async Task<PhotoUploadUrlResponse> HandleAsync(
        Guid callerId,
        string contentType,
        CancellationToken cancellationToken)
    {
        var extension = ExtensionFor(contentType);
        var objectKey = $"profiles/{callerId}/photo/{Guid.NewGuid()}.{extension}";

        var target = await _photoStorage.CreateUploadUrlAsync(objectKey, contentType, cancellationToken);

        return new PhotoUploadUrlResponse(target.UploadUrl, objectKey, target.ExpiresAt);
    }

    private static string ExtensionFor(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => throw new ArgumentOutOfRangeException(
                nameof(contentType), contentType, "Unsupported content type."),
        };
    }
}
