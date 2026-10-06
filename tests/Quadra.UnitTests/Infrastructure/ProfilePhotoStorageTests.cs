using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quadra.Infrastructure.DependencyInjection;
using Quadra.Infrastructure.Storage;

namespace Quadra.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for the optional profile-photo storage: with no bucket configured the API runs
/// without any object storage; with one it presigns URLs against AWS S3 or any S3-compatible
/// endpoint. URL presigning is a local computation — no network is used.
/// </summary>
public sealed class ProfilePhotoStorageTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddProfilePhotoStorage(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static readonly Dictionary<string, string?> CompatibleEndpointSettings = new()
    {
        ["Aws:S3:ProfilePhotosBucket"] = "quadra-photos",
        ["Aws:S3:ServiceUrl"] = "https://account-id.r2.cloudflarestorage.com",
        ["Aws:S3:AccessKeyId"] = "test-access-key",
        ["Aws:S3:SecretAccessKey"] = "test-secret-key",
    };

    /// <summary>
    /// Covers: no bucket → photos are disabled. Reading yields no URL (profiles are served
    /// without a photo) and an upload request is refused with a dedicated exception.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_bucket_storage_is_unconfigured(string? bucket)
    {
        await using var provider = BuildProvider(new() { ["Aws:S3:ProfilePhotosBucket"] = bucket });
        await using var scope = provider.CreateAsyncScope();

        var storage = scope.ServiceProvider.GetRequiredService<IProfilePhotoStorage>();

        storage.Should().BeOfType<UnconfiguredProfilePhotoStorage>();
        (await storage.GetReadUrlAsync("profiles/x/photo.jpg", TestContext.Current.CancellationToken))
            .Should().BeNull();
        var upload = async () => await storage.CreateUploadUrlAsync(
            "profiles/x/photo.jpg", "image/jpeg", TestContext.Current.CancellationToken);
        await upload.Should().ThrowAsync<ProfilePhotoStorageUnavailableException>();
    }

    /// <summary>
    /// Covers: an S3-compatible endpoint (e.g. Cloudflare R2) — upload and read URLs are presigned
    /// against that endpoint, path-style, for the configured bucket and object key.
    /// </summary>
    [Fact]
    public async Task With_a_compatible_endpoint_urls_are_presigned_against_it()
    {
        await using var provider = BuildProvider(CompatibleEndpointSettings);
        await using var scope = provider.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IProfilePhotoStorage>();

        var upload = await storage.CreateUploadUrlAsync(
            "profiles/abc/photo/1.jpg", "image/jpeg", TestContext.Current.CancellationToken);
        var read = await storage.GetReadUrlAsync("profiles/abc/photo/1.jpg", TestContext.Current.CancellationToken);

        storage.Should().BeOfType<S3ProfilePhotoStorage>();
        const string expectedPrefix =
            "https://account-id.r2.cloudflarestorage.com/quadra-photos/profiles/abc/photo/1.jpg?";
        upload.UploadUrl.Should().StartWith(expectedPrefix).And.Contain("X-Amz-Signature=");
        read.Should().StartWith(expectedPrefix).And.Contain("X-Amz-Signature=");
        upload.UploadUrl.Should().NotContain("test-secret-key");
        upload.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Covers: a compatible endpoint without credentials fails validation instead of signing
    /// with nothing.
    /// </summary>
    [Fact]
    public void Compatible_endpoint_without_credentials_fails_validation()
    {
        var settings = new Dictionary<string, string?>(CompatibleEndpointSettings)
        {
            ["Aws:S3:SecretAccessKey"] = "",
        };
        using var provider = BuildProvider(settings);

        var act = () => provider.GetRequiredService<IOptions<ProfilePhotoStorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*SecretAccessKey*");
    }

    /// <summary>
    /// Covers: non-positive TTLs fall back to the defaults (15 min upload, 60 min read).
    /// </summary>
    [Fact]
    public void Non_positive_ttls_fall_back_to_defaults()
    {
        using var provider = BuildProvider(new()
        {
            ["Aws:S3:PhotoUploadUrlTtlMinutes"] = "0",
            ["Aws:S3:PhotoReadUrlTtlMinutes"] = "-5",
        });

        var options = provider.GetRequiredService<IOptions<ProfilePhotoStorageOptions>>().Value;

        options.PhotoUploadUrlTtlMinutes.Should().Be(15);
        options.PhotoReadUrlTtlMinutes.Should().Be(60);
    }
}
