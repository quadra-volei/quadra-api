using Microsoft.EntityFrameworkCore;
using Quadra.Api;
using Quadra.Api.Background;
using Quadra.Api.Events;
using Quadra.Modules.Auth.DependencyInjection;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Gamification.DependencyInjection;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Modules.Geo.DependencyInjection;
using Quadra.Modules.InGame.DependencyInjection;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.DependencyInjection;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Notifications;
using Quadra.Modules.Profile.DependencyInjection;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Realtime;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Errors and traces to Sentry, configured from the "Sentry" section; off while Sentry:Dsn is empty.
// Render sets RENDER_GIT_COMMIT, so each event names the deployed commit.
builder.WebHost.UseSentry(options => options.Release ??= Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT"));

builder.Services.AddControllers();
// Unhandled exceptions answer ProblemDetails without internals; unique-index races answer 409.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UniqueViolationExceptionHandler>();
// SEC-07: the modules add their policies (login/sms-otp per IP, places per account).
builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
// Before the modules: they fall back to a no-op publisher only when none is registered.
builder.Services.AddInProcessEventHandling();
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddMatchesModule(builder.Configuration);
builder.Services.AddInGameModule(builder.Configuration);
builder.Services.AddProfileModule(builder.Configuration);
builder.Services.AddGeoModule(builder.Configuration);
builder.Services.AddGamificationModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);

// Live match updates over SignalR. Registered after the modules: it replaces their no-op notifier.
builder.Services.AddRealtimeModule();
builder.Services.AddHostedService<MatchWindowSweeper>();
builder.Services.AddSingleton<MatchReminderSweeper>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchReminderSweeper>());
builder.Services.AddOpenApi();

var app = builder.Build();

// Opt-in (Database:MigrateOnStartup=true): apply pending EF migrations before serving traffic.
// Meant for the single-instance hosted environment, where nobody runs `dotnet ef` by hand.
// Leave it off when more than one instance can start at the same time.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await services.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<MatchesDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<InGameDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<ProfileDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<GamificationDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseExceptionHandler();
app.UseRouting();
app.UseAuthModule();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.MapControllers();
app.MapRealtimeHubs();

await app.RunAsync();

public partial class Program;
