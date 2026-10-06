using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quadra.Infrastructure.Storage;

namespace Quadra.Infrastructure.DependencyInjection;

/// <summary>
/// Composition-root helpers for infrastructure-owned external services. Modules that need an
/// infrastructure abstraction (e.g. Profile needing S3 photo storage) call the relevant method
/// from their own <c>Add&lt;Module&gt;Module</c> extension.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Registers <see cref="IProfilePhotoStorage"/> backed by S3 and binds its options from the
    /// <c>Aws:S3</c> configuration section. Fails fast if the bucket name is missing.
    /// </summary>
    public static IServiceCollection AddProfilePhotoStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var bucket = configuration["Aws:S3:ProfilePhotosBucket"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:S3:ProfilePhotosBucket' is required but was not found.");

        var options = new ProfilePhotoStorageOptions
        {
            ProfilePhotosBucket = bucket,
            PhotoUploadUrlTtlMinutes = ParsePositiveInt(configuration["Aws:S3:PhotoUploadUrlTtlMinutes"], 15),
            PhotoReadUrlTtlMinutes = ParsePositiveInt(configuration["Aws:S3:PhotoReadUrlTtlMinutes"], 60),
        };

        services.TryAddSingleton<IOptions<ProfilePhotoStorageOptions>>(Options.Create(options));
        services.TryAddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IProfilePhotoStorage, S3ProfilePhotoStorage>();

        return services;
    }

    private static int ParsePositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
