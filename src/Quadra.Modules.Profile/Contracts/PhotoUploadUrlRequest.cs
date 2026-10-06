namespace Quadra.Modules.Profile.Contracts;

/// <summary>Request body for <c>POST /api/v1/profiles/me/photo/upload-url</c>.</summary>
public sealed record PhotoUploadUrlRequest(
    string ContentType);
