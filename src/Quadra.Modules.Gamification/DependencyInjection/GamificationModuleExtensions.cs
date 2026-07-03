using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Gamification.Premium;
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

        // MVP premium check — a stub returning free tier for every player (no billing in the MVP).
        services.AddScoped<IPremiumStatusReader, FreeTierPremiumStatusReader>();

        return services;
    }
}
