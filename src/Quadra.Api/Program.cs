using Microsoft.EntityFrameworkCore;
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
using Quadra.Modules.Profile.DependencyInjection;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Realtime;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Before the modules: they fall back to a no-op publisher only when none is registered.
builder.Services.AddInProcessEventHandling();
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddMatchesModule(builder.Configuration);
builder.Services.AddInGameModule(builder.Configuration);
builder.Services.AddProfileModule(builder.Configuration);
builder.Services.AddGeoModule(builder.Configuration);
builder.Services.AddGamificationModule(builder.Configuration);

// Live match updates over SignalR. Registered after the modules: it replaces their no-op notifier.
builder.Services.AddRealtimeModule();
builder.Services.AddHostedService<MatchWindowSweeper>();
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
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRouting();
app.UseAuthModule();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.MapControllers();
app.MapRealtimeHubs();

await app.RunAsync();

public partial class Program;
