using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Validation;

namespace Quadra.Modules.Geo.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Geo module.
/// All DI registrations for this module go exclusively through <see cref="AddGeoModule"/>.
/// </summary>
public static class GeoModuleExtensions
{
    public static IServiceCollection AddGeoModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Read-only EF DbContext — reads from ConnectionStrings:Geo, falling back to ConnectionStrings:Default.
        services.AddDbContext<GeoReadDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("Geo")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Geo (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<INearbyMatchRepository, NearbyMatchRepository>();
        services.AddScoped<GetNearbyMatchesHandler>();

        services.AddScoped<IValidator<NearbyMatchesQuery>, NearbyMatchesQueryValidator>();

        // TimeProvider.System — register only if not already registered by another module.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
