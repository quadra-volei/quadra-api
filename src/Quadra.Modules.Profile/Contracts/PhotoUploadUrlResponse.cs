namespace Quadra.Modules.Profile.Contracts;

/// <summary>Response for a presigned photo-upload URL request.</summary>
public sealed record PhotoUploadUrlResponse(
    string UploadUrl,
    string ObjectKey,
    DateTimeOffset ExpiresAt);
