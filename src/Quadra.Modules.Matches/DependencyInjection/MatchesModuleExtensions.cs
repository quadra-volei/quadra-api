using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Matches.Validation;

namespace Quadra.Modules.Matches.DependencyInjection;

/// <summary>
/// Composition-root entry point for the Matches module.
/// All DI registrations for this module go exclusively through <see cref="AddMatchesModule"/>.
/// </summary>
public static class MatchesModuleExtensions
{
    public static IServiceCollection AddMatchesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail-fast: SQS queue URLs must be configured before the app starts.
        var matchCreatedQueue = configuration["Aws:Sqs:MatchCreatedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchCreatedQueueUrl' is required but was not found.");

        var matchStatusChangedQueue = configuration["Aws:Sqs:MatchStatusChangedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchStatusChangedQueueUrl' is required but was not found.");

        _ = matchCreatedQueue;        // validated above; consumed by the SQS publisher (future spec)
        _ = matchStatusChangedQueue;  // validated above; consumed by the SQS publisher (future spec)

        // EF DbContext — reads from ConnectionStrings:Matches, falling back to ConnectionStrings:Default.
        services.AddDbContext<MatchesDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString =
                cfg.GetConnectionString("Matches")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Matches (or ConnectionStrings:Default) must be configured.");

            builder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<IMatchRepository, MatchRepository>();

        services.AddScoped<CreateMatchHandler>();
        services.AddScoped<GetMatchHandler>();
        services.AddScoped<ListMatchesHandler>();
        services.AddScoped<CancelMatchHandler>();

        services.AddScoped<IValidator<CreateMatchRequest>, CreateMatchRequestValidator>();
        services.AddScoped<IValidator<ListMatchesQuery>, ListMatchesQueryValidator>();

        // TimeProvider.System — register only if not already registered (Auth module may have done so).
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
