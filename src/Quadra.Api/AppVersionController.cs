using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Quadra.Api;

/// <summary>
/// Which app versions may still use the API (DECISIONS #74). Below <c>minimumVersion</c> the app
/// blocks with "atualize o app"; below <c>recommendedVersion</c> it only suggests the update.
/// Read before login, so it is anonymous. Values come from <c>App:MinimumVersion</c> and
/// <c>App:RecommendedVersion</c> (Render: <c>App__MinimumVersion</c> …); empty = no requirement.
/// </summary>
[ApiController]
[Route("api/v1/app/version")]
[AllowAnonymous]
public sealed class AppVersionController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppVersionController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public AppVersionResponse Get() => new(
        Blank(_configuration["App:MinimumVersion"]),
        Blank(_configuration["App:RecommendedVersion"]),
        _configuration["App:StoreUrl"] ?? "https://play.google.com/store/apps/details?id=com.quadra.app");

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="MinimumVersion">Oldest app version that still works (e.g. <c>1.2.0</c>); null = any.</param>
/// <param name="RecommendedVersion">Version the app should suggest updating to; null = none.</param>
/// <param name="StoreUrl">Where the update is.</param>
public sealed record AppVersionResponse(string? MinimumVersion, string? RecommendedVersion, string StoreUrl);
