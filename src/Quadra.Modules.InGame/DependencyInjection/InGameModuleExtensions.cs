using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Infrastructure.Contracts;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.DependencyInjection;

/// <summary>
/// Composition-root entry point for the InGame module.
/// All DI registrations for this module go exclusively through <see cref="AddInGameModule"/>.
/// </summary>
public static class InGameModuleExtensions
{
    public static IServiceCollection AddInGameModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail-fast: SQS queue URL must be configured before the app starts.
        _ = configuration["Aws:Sqs:TeamsFormedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:TeamsFormedQueueUrl' is required but was not found.");

        // EF DbContext — reads from ConnectionStrings:InGame, falling back to ConnectionStrings:Default.
        services.AddDbContext<InGameDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("InGame")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:InGame (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<ITeamRepository, TeamRepository>();

        services.AddScoped<DraftTeamsHandler>();
        services.AddScoped<GetTeamsHandler>();
        services.AddScoped<MovePlayerHandler>();

        // IPlayerLevelReader — no-op until F2.1 (Profile module) delivers the real implementation.
        // TryAddScoped allows the Profile module to override this at composition time.
        services.TryAddScoped<IPlayerLevelReader, NoOpPlayerLevelReader>();

        // TimeProvider.System — register only if not already registered.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
