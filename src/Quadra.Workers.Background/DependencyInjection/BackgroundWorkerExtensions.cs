using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.InGame.DependencyInjection;
using Quadra.Modules.Matches.DependencyInjection;
using Quadra.Modules.Profile.DependencyInjection;
using Quadra.Workers.Background.Consumers;

namespace Quadra.Workers.Background.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Background Worker's event-consumption surface.
/// Wires the modules whose read/write interfaces the consumers depend on (Profile writes,
/// Matches and InGame cross-module reads) and registers the consumer classes themselves.
/// </summary>
public static class BackgroundWorkerExtensions
{
    public static IServiceCollection AddProfileEventConsumers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail-fast: the worker's consumed queue URLs must be configured.
        _ = configuration["Aws:Sqs:UserRegisteredQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:UserRegisteredQueueUrl' is required but was not found.");

        _ = configuration["Aws:Sqs:MatchSummaryGeneratedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchSummaryGeneratedQueueUrl' is required but was not found.");

        // Modules exposing the interfaces the consumers resolve.
        services.AddProfileModule(configuration);   // IPlayerProfileProvisioner, IPlayerStatsWriter
        services.AddMatchesModule(configuration);   // IMatchDescriptorReader
        services.AddInGameModule(configuration);    // IMatchResultReader

        services.AddScoped<UserRegisteredConsumer>();
        services.AddScoped<MatchSummaryGeneratedProfileConsumer>();

        return services;
    }
}
