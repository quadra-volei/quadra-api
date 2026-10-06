using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quadra.Infrastructure.Storage;

namespace Quadra.Infrastructure.DependencyInjection;

/// <summary>
/// Composition-root helpers for infrastructure-owned external services. Modules that need an
/// infrastructure abstraction (e.g. Profile needing photo storage) call the relevant method
/// from their own <c>Add&lt;Module&gt;Module</c> extension.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Registers <see cref="IProfilePhotoStorage"/> and binds its options from the <c>Aws:S3</c>
    /// configuration section. With no bucket configured the storage is the no-photo
    /// <see cref="UnconfiguredProfilePhotoStorage"/>, so the API runs without any object storage;
    /// with a bucket it is S3 (AWS, or any S3-compatible service when <c>ServiceUrl</c> is set).
    /// </summary>
    public static IServiceCollection AddProfilePhotoStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bound lazily through DI so hosts that add configuration after this call still see it.
        services
            .AddOptions<ProfilePhotoStorageOptions>()
            .Configure<IConfiguration>(static (options, cfg) =>
            {
                var section = cfg.GetSection(ProfilePhotoStorageOptions.SectionName);
                options.ProfilePhotosBucket = section[nameof(options.ProfilePhotosBucket)] ?? string.Empty;
                options.ServiceUrl = section[nameof(options.ServiceUrl)];
                options.AccessKeyId = section[nameof(options.AccessKeyId)];
                options.SecretAccessKey = section[nameof(options.SecretAccessKey)];

                if (!string.IsNullOrWhiteSpace(section[nameof(options.Region)]))
                {
                    options.Region = section[nameof(options.Region)]!;
                }

                if (int.TryParse(section[nameof(options.PhotoUploadUrlTtlMinutes)], out var uploadTtl))
                {
                    options.PhotoUploadUrlTtlMinutes = uploadTtl;
                }

                if (int.TryParse(section[nameof(options.PhotoReadUrlTtlMinutes)], out var readTtl))
                {
                    options.PhotoReadUrlTtlMinutes = readTtl;
                }
            })
            .Validate(
                static options => !options.IsConfigured
                    || !options.UsesCustomEndpoint
                    || (!string.IsNullOrWhiteSpace(options.AccessKeyId)
                        && !string.IsNullOrWhiteSpace(options.SecretAccessKey)),
                "Aws:S3:AccessKeyId and Aws:S3:SecretAccessKey are required when Aws:S3:ServiceUrl is set.")
            .ValidateOnStart();

        // Only resolved when a bucket is configured, so an unconfigured host never needs AWS settings.
        services.TryAddSingleton<IAmazonS3>(static sp =>
            CreateS3Client(sp.GetRequiredService<IOptions<ProfilePhotoStorageOptions>>().Value));
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<IProfilePhotoStorage>(static sp =>
        {
            var options = sp.GetRequiredService<IOptions<ProfilePhotoStorageOptions>>();
            return options.Value.IsConfigured
                ? new S3ProfilePhotoStorage(
                    sp.GetRequiredService<IAmazonS3>(),
                    options,
                    sp.GetRequiredService<TimeProvider>())
                : new UnconfiguredProfilePhotoStorage();
        });

        return services;
    }

    /// <summary>
    /// Builds the S3 client: the default AWS client, or one pointed at an S3-compatible endpoint
    /// with explicit credentials (path-style addressing, which those services expect).
    /// </summary>
    public static IAmazonS3 CreateS3Client(ProfilePhotoStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.UsesCustomEndpoint)
        {
            return new AmazonS3Client();
        }

        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = true,
        };

        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            config);
    }
}
