using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.Infrastructure.Contracts;
using Quadra.Infrastructure.Persistence;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.InGame.Validation;
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

        // Fail-fast: SQS queue URLs must be configured before the app starts.
        _ = configuration["Aws:Sqs:TeamsFormedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:TeamsFormedQueueUrl' is required but was not found.");

        _ = configuration["Aws:Sqs:MatchStartedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchStartedQueueUrl' is required but was not found.");

        _ = configuration["Aws:Sqs:MatchEndedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MatchEndedQueueUrl' is required but was not found.");

        _ = configuration["Aws:Sqs:MvpAwardedQueueUrl"]
            ?? throw new InvalidOperationException(
                "Configuration key 'Aws:Sqs:MvpAwardedQueueUrl' is required but was not found.");

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
                PostgresConnectionString.Normalize(connectionString),
                npgsql => npgsql.UseNetTopologySuite());
        });

        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IScoreboardRepository, ScoreboardRepository>();
        services.AddScoped<IMvpVotingRepository, MvpVotingRepository>();

        // Cross-module read interface implemented by the InGame module (consumed by Matches, F1.6).
        services.AddScoped<IMatchResultReader, MatchResultReader>();

        services.AddScoped<DraftTeamsHandler>();
        services.AddScoped<GetTeamsHandler>();
        services.AddScoped<MovePlayerHandler>();

        services.AddScoped<CreateScoreboardHandler>();
        services.AddScoped<StartScoreboardHandler>();
        services.AddScoped<RecordPointHandler>();
        services.AddScoped<EndScoreboardHandler>();
        services.AddScoped<UndoPointHandler>();
        services.AddScoped<EndSetHandler>();
        services.AddScoped<OpenNextSetHandler>();
        services.AddScoped<GetScoreboardHandler>();

        services.AddScoped<OpenMvpVotingHandler>();
        // Reacts to the game ending (in-process event) by opening the voting.
        services.AddScoped<Quadra.Infrastructure.Messaging.IEventHandler<Quadra.Shared.Events.InGame.MatchEnded>, OpenMvpVotingOnMatchEnded>();
        services.AddScoped<CastMvpVoteHandler>();
        services.AddScoped<CloseMvpVotingHandler>();
        services.AddScoped<GetMvpVotingHandler>();

        services.AddScoped<IValidator<CreateScoreboardRequest>, CreateScoreboardRequestValidator>();
        services.AddScoped<IValidator<RecordPointRequest>, RecordPointRequestValidator>();
        services.AddScoped<IValidator<OpenMvpVotingRequest>, OpenMvpVotingRequestValidator>();
        services.AddScoped<IValidator<CastMvpVoteRequest>, CastMvpVoteRequestValidator>();

        // IPlayerLevelReader — no-op until F2.1 (Profile module) delivers the real implementation.
        // TryAddScoped allows the Profile module to override this at composition time.
        services.TryAddScoped<IPlayerLevelReader, NoOpPlayerLevelReader>();

        // TimeProvider.System — register only if not already registered.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
