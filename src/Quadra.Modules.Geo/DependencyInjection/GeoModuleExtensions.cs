using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quadra.Infrastructure.Persistence;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Places;
using Quadra.Modules.Geo.Validation;
using Quadra.Modules.Geo.Venues;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Geo.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Geo module.
/// All DI registrations for this module go exclusively through <see cref="AddGeoModule"/>.
/// </summary>
public static class GeoModuleExtensions
{
    // The search runs while the user types: a slow provider must fail fast.
    private static readonly TimeSpan PlaceSearchTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddGeoModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Read-only EF DbContext — reads from ConnectionStrings:Geo, falling back to ConnectionStrings:Default.
        services.AddDbContext<GeoDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("Geo")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Geo (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(
                PostgresConnectionString.Normalize(connectionString),
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<INearbyMatchRepository, NearbyMatchRepository>();
        services.AddScoped<GetNearbyMatchesHandler>();

        services.AddScoped<IValidator<NearbyMatchesQuery>, NearbyMatchesQueryValidator>();

        // Address search (create-match "LOCAL" field) — provider chosen by the Places section.
        services
            .AddOptions<PlacesOptions>()
            .BindConfiguration(PlacesOptions.SectionName)
            .Validate(
                static options => options.IsValid,
                "Places:Provider must be Google, OpenStreetMap or None, and Google needs Places:GoogleApiKey.")
            .ValidateOnStart();

        services.AddRateLimiter(options => options.AddPolicy(PlacesOptions.RateLimitPolicy, context =>
        {
            var limit = context.RequestServices.GetRequiredService<IOptions<PlacesOptions>>().Value.RequestsPerMinute;
            return RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        }));

        services
            .AddHttpClient(GooglePlaceSearchService.HttpClientName)
            .ConfigureHttpClient(static client => client.Timeout = PlaceSearchTimeout);
        services
            .AddHttpClient(PhotonPlaceSearchService.HttpClientName)
            .ConfigureHttpClient(static client =>
            {
                client.Timeout = PlaceSearchTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("quadra-api/1.0");
            });

        services.AddSingleton<GooglePlaceSearchService>();
        services.AddSingleton<PhotonPlaceSearchService>();
        services.AddSingleton<NoPlaceSearchService>();
        services.AddSingleton<IPlaceSearchService>(static sp =>
            sp.GetRequiredService<IOptions<PlacesOptions>>().Value.ResolvedProvider switch
            {
                PlacesOptions.Google => sp.GetRequiredService<GooglePlaceSearchService>(),
                PlacesOptions.OpenStreetMap => sp.GetRequiredService<PhotonPlaceSearchService>(),
                _ => sp.GetRequiredService<NoPlaceSearchService>(),
            });

        // Arenas on the map (DECISIONS #82): only Google knows them; without it the map shows none.
        services.AddSingleton<NoVenueSource>();
        services.AddSingleton<IVenueSource>(static sp =>
            sp.GetRequiredService<IOptions<PlacesOptions>>().Value.ResolvedProvider == PlacesOptions.Google
                ? sp.GetRequiredService<GooglePlaceSearchService>()
                : sp.GetRequiredService<NoVenueSource>());
        services.AddScoped<VenueDiscovery>();
        services.AddScoped<GetNearbyVenuesHandler>();

        // Curated arenas (DECISIONS #91, #92): Matches links games to them through IArenaReader.
        services.AddScoped<ArenaReader>();
        services.AddScoped<IArenaReader>(static sp => sp.GetRequiredService<ArenaReader>());

        // TimeProvider.System — register only if not already registered by another module.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
