using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Infrastructure.Contracts;
using Quadra.Infrastructure.Persistence;
using Quadra.Infrastructure.Realtime;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Matches.Validation;
using Quadra.Shared.Contracts;
using Quadra.Shared.Realtime;

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

        var presenceConfirmedQueue = configuration["Aws:Sqs:PresenceConfirmedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:PresenceConfirmedQueueUrl' is required but was not found.");

        var matchWindowOpenedQueue = configuration["Aws:Sqs:MatchWindowOpenedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchWindowOpenedQueueUrl' is required but was not found.");

        var matchWindowClosedQueue = configuration["Aws:Sqs:MatchWindowClosedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchWindowClosedQueueUrl' is required but was not found.");

        var matchSummaryGeneratedQueue = configuration["Aws:Sqs:MatchSummaryGeneratedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchSummaryGeneratedQueueUrl' is required but was not found.");

        _ = matchCreatedQueue;          // validated above; consumed by the SQS publisher (future spec)
        _ = matchStatusChangedQueue;    // validated above; consumed by the SQS publisher (future spec)
        _ = presenceConfirmedQueue;     // validated above; consumed by the SQS publisher (future spec)
        _ = matchWindowOpenedQueue;     // validated above; consumed by the SQS publisher (future spec)
        _ = matchWindowClosedQueue;     // validated above; consumed by the SQS publisher (future spec)
        _ = matchSummaryGeneratedQueue; // validated above; consumed by the SQS publisher (future spec)

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
                PostgresConnectionString.Normalize(connectionString),
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IPresenceRepository, PresenceRepository>();
        services.AddScoped<IWaitingListRepository, WaitingListRepository>();
        services.AddScoped<IMatchSummaryRepository, MatchSummaryRepository>();
        services.AddScoped<IMatchGuestRepository, MatchGuestRepository>();

        // Cross-module read interfaces implemented by the Matches module.
        services.AddScoped<IMatchReader, MatchReader>();
        services.AddScoped<IConfirmedPlayersReader, ConfirmedPlayersReader>();
        services.AddScoped<IMatchAvailabilityReader, MatchAvailabilityReader>();
        services.AddScoped<IMatchDescriptorReader, MatchDescriptorReader>();
        services.AddScoped<IMatchGroupReader, MatchGroupReader>();

        services.AddScoped<CreateMatchHandler>();
        services.AddScoped<GetMatchHandler>();
        services.AddScoped<ListMatchesHandler>();
        services.AddScoped<CancelMatchHandler>();
        services.AddScoped<AddPresenceHandler>();
        services.AddScoped<UpdateMyPresenceHandler>();
        services.AddScoped<GetPresenceListHandler>();
        services.AddScoped<RemovePresenceHandler>();
        services.AddScoped<OpenMatchWindowHandler>();
        services.AddScoped<CloseMatchWindowHandler>();
        services.AddScoped<GenerateMatchSummaryHandler>();
        services.AddScoped<GetMatchSummaryHandler>();
        services.AddScoped<MatchWindowSynchronizer>();
        services.AddScoped<GetMatchDetailHandler>();
        services.AddScoped<ListMyMatchesHandler>();
        services.AddScoped<AddMatchGuestHandler>();
        services.AddScoped<RemoveMatchGuestHandler>();

        services.AddScoped<IValidator<CreateMatchRequest>, CreateMatchRequestValidator>();
        services.AddScoped<IValidator<ListMatchesQuery>, ListMatchesQueryValidator>();
        services.AddScoped<IValidator<AddPresenceRequest>, AddPresenceRequestValidator>();
        services.AddScoped<IValidator<UpdateMyPresenceRequest>, UpdateMyPresenceRequestValidator>();
        services.AddScoped<IValidator<AddGuestRequest>, AddGuestRequestValidator>();

        // Player identities for rosters come from Profile; hosts without it get placeholders.
        services.TryAddScoped<IPlayerSummaryReader, NoOpPlayerSummaryReader>();

        // IMatchRoomNotifier — no-op until the SignalR implementation is delivered by Quadra.Modules.Realtime.
        services.TryAddSingleton<IMatchRoomNotifier, NoOpMatchRoomNotifier>();

        // TimeProvider.System — register only if not already registered (Auth module may have done so).
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
