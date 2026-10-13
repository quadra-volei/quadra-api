using Quadra.Infrastructure.Messaging;
using Quadra.Shared.Events.Auth;
using Quadra.Shared.Events.Matches;

namespace Quadra.Api.Events;

/// <summary>
/// Wires in-process event delivery: the API handles its own integration events instead of
/// sending them to a queue for a separate worker. The handlers live in the host because they
/// coordinate several modules through their public interfaces.
/// </summary>
public static class EventHandlingExtensions
{
    /// <summary>
    /// Registers <see cref="InProcessEventPublisher"/> and the event handlers. Call it before the
    /// modules are added: they only register the no-op publisher when none is present.
    /// </summary>
    public static IServiceCollection AddInProcessEventHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEventPublisher, InProcessEventPublisher>();

        services.AddScoped<IEventHandler<UserRegistered>, UserRegisteredConsumer>();
        services.AddScoped<IEventHandler<MatchSummaryGenerated>, MatchSummaryGeneratedProfileConsumer>();
        services.AddScoped<IEventHandler<MatchSummaryGenerated>, MatchSummaryGeneratedRankingConsumer>();

        return services;
    }
}
