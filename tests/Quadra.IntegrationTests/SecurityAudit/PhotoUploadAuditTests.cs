using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Upload URLs are limited per account (each one lets the caller put an object in the bucket).
/// The publish-on-save rule that stops a reused upload URL from replacing a checked photo is
/// unit-tested in PhotoSizeLimitTests (this fixture has no bucket).
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class PhotoUploadAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public PhotoUploadAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // quadra-api:storage:unconfirmed-upload-unbounded-storage
    [Fact]
    public async Task Upload_urls_are_rate_limited_per_account()
    {
        using var host = _fx.Create("Testing", new Dictionary<string, string?> { ["Profiles:PhotoUploadsPerHour"] = "3" });
        var p = await _fx.LoginAsync(host);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add((await p.Client.PostAsJsonAsync("/api/v1/profiles/me/photo/upload-url", new { contentType = "image/jpeg" }, Ct)).StatusCode);
        }

        codes[3].Should().Be(HttpStatusCode.TooManyRequests, $"codes: {string.Join(',', codes)}");
    }
}
