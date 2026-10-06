using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Infrastructure.DependencyInjection;
using Quadra.Infrastructure.Persistence;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Validation;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Profile module.
/// All DI registrations for this module go exclusively through <see cref="AddProfileModule"/>.
/// </summary>
public static class ProfileModuleExtensions
{
    public static IServiceCollection AddProfileModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // EF DbContext — reads from ConnectionStrings:Profile, falling back to ConnectionStrings:Default.
        services.AddDbContext<ProfileDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("Profile")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Profile (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(
                PostgresConnectionString.Normalize(connectionString),
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<IPlayerProfileRepository, PlayerProfileRepository>();
        services.AddScoped<IPlayerMatchHistoryRepository, PlayerMatchHistoryRepository>();
        services.AddScoped<IPlayerCardRepository, PlayerCardRepository>();

        services.AddScoped<GetProfileHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<GetMatchHistoryHandler>();
        services.AddScoped<CreatePhotoUploadUrlHandler>();
        services.AddScoped<GetPlayerCardHandler>();

        // Profile-owned write interfaces (invoked by the Background Worker).
        services.AddScoped<IPlayerProfileProvisioner, ProvisionProfileHandler>();
        services.AddScoped<IPlayerStatsWriter, ApplyFinishedMatchHandler>();

        // Real player-level reader (F2.1). AddScoped overrides the F1.3 NoOpPlayerLevelReader
        // registered via TryAddScoped, so team drafting now balances on real levels.
        services.AddScoped<IPlayerLevelReader, PlayerLevelReader>();

        services.AddScoped<IValidator<UpdateProfileRequest>, UpdateProfileRequestValidator>();
        services.AddScoped<IValidator<PhotoUploadUrlRequest>, PhotoUploadUrlRequestValidator>();
        services.AddScoped<IValidator<MatchHistoryQuery>, MatchHistoryQueryValidator>();

        // S3-backed profile-photo storage (Infrastructure). Fails fast if the bucket is unconfigured.
        services.AddProfilePhotoStorage(configuration);

        // TimeProvider.System — register only if not already registered.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
