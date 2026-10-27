using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Infrastructure.Persistence;
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

            builder.UseNpgsql(PostgresConnectionString.Normalize(connectionString));
        });

        services.AddScoped<IPointTransactionRepository, PointTransactionRepository>();
        services.AddScoped<IGroupRankingRepository, GroupRankingRepository>();
        services.AddScoped<Quadra.Shared.Contracts.IPersonalDataStore, GamificationPersonalData>();

        // Gamification-owned write interface (invoked by the Background Worker).
        services.AddScoped<IMatchPointsWriter, ApplyFinishedMatchPointsHandler>();

        services.AddScoped<GetGroupRankingHandler>();
        services.AddScoped<GetMyRankingHandler>();
        services.AddScoped<GetRankingHandler>();
        services.AddScoped<IValidator<RankingQuery>, RankingQueryValidator>();
        // Player identities for the ranking rows; Profile registers the real reader.
        services.TryAddScoped<Quadra.Shared.Contracts.IPlayerSummaryReader, Quadra.Infrastructure.Contracts.NoOpPlayerSummaryReader>();

        services.AddScoped<IValidator<GroupRankingQuery>, GroupRankingQueryValidator>();

        // MVP premium check — a stub returning free tier for every player (no billing in the MVP).
        services.AddScoped<IPremiumStatusReader, FreeTierPremiumStatusReader>();

        // Season pass (DECISIONS #98): the seasons' dates and the paid track switch come from configuration.
        services.AddOptions<SeasonPassOptions>()
            .BindConfiguration(SeasonPassOptions.SectionName)
            .Validate(o => o.IsValid(), "SeasonPass:Seasons: unknown season code, a season that ends before it starts, or overlapping seasons.")
            .ValidateOnStart();
        services.AddScoped<SeasonPassHandler>();
        services.AddScoped<ISeasonRewardReader>(sp => sp.GetRequiredService<SeasonPassHandler>());

        // TimeProvider.System — register only if not already registered.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
