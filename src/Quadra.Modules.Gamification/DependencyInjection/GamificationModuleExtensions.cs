using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Modules.Gamification.Premium;
using Quadra.Modules.Gamification.Validation;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Gamification module.
/// All DI registrations for this module go exclusively through <see cref="AddGamificationModule"/>.
/// </summary>
public static class GamificationModuleExtensions
{
    public static IServiceCollection AddGamificationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // EF DbContext — reads from ConnectionStrings:Gamification, falling back to ConnectionStrings:Default.
        // Fails fast at startup if neither is configured.
        services.AddDbContext<GamificationDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("Gamification")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Gamification (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(connectionString);
        });

        services.AddScoped<IPointTransactionRepository, PointTransactionRepository>();
        services.AddScoped<IGroupRankingRepository, GroupRankingRepository>();

        // Gamification-owned write interface (invoked by the Background Worker).
        services.AddScoped<IMatchPointsWriter, ApplyFinishedMatchPointsHandler>();

        services.AddScoped<GetGroupRankingHandler>();

        services.AddScoped<IValidator<GroupRankingQuery>, GroupRankingQueryValidator>();

        // MVP premium check — a stub returning free tier for every player (no billing in the MVP).
        services.AddScoped<IPremiumStatusReader, FreeTierPremiumStatusReader>();

        // TimeProvider.System — register only if not already registered.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
