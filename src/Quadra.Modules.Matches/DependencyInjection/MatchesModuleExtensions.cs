using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
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
    public const string CreateRateLimitPolicy = "match-create";

    public static IServiceCollection AddMatchesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // New matches per organizer per day: accounts are self-service, and each forgotten recurring
        // match renews itself into the abandoned sweep (audit 2026-11-24, sweeper starvation).
        // ponytail: in memory, resets when the instance restarts; enough with one instance.
        services.AddRateLimiter(options => options.AddPolicy(CreateRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Matches:CreatePerDay", 20),
                    Window = TimeSpan.FromDays(1),
                    QueueLimit = 0,
                })));

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
        services.AddScoped<IMatchInviteReader, MatchInviteReader>();
        services.AddScoped<IMatchCoverReader, MatchCoverReader>();
        services.AddScoped<IMatchGroupReader, MatchGroupReader>();
        services.AddScoped<IMatchAccessReader, MatchAccessReader>();

        services.AddScoped<MatchArenaResolver>();
        services.AddScoped<CreateMatchHandler>();
        services.AddScoped<GetMatchHandler>();
        services.AddScoped<ListMatchesHandler>();
        services.AddScoped<CancelMatchHandler>();
        services.AddScoped<IPersonalDataStore, MatchesPersonalData>();
        services.AddScoped<IUpcomingMatchReader, UpcomingMatchReader>();
        services.AddScoped<UpdateMatchHandler>();
        services.AddScoped<UpdateMyPresenceHandler>();
        services.AddScoped<GetPresenceListHandler>();
        services.AddScoped<RemovePresenceHandler>();
        services.AddScoped<LeaveWaitingListHandler>();
        services.AddScoped<OpenMatchWindowHandler>();
        services.AddScoped<CloseMatchWindowHandler>();
        services.AddScoped<GenerateMatchSummaryHandler>();
        services.AddScoped<ScheduleNextGameHandler>();
        services.AddScoped<GetMatchSummaryHandler>();
        services.AddScoped<MatchWindowSynchronizer>();
        services.AddScoped<GetMatchDetailHandler>();
        services.AddScoped<ListMyMatchesHandler>();
        services.AddScoped<AddMatchGuestHandler>();
        services.AddScoped<RemoveMatchGuestHandler>();

        services.AddScoped<IValidator<CreateMatchRequest>, CreateMatchRequestValidator>();
        services.AddScoped<IValidator<UpdateMatchRequest>, UpdateMatchRequestValidator>();
        services.AddScoped<IValidator<ListMatchesQuery>, ListMatchesQueryValidator>();
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
