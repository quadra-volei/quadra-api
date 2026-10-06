using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Auth.DependencyInjection;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Gamification.DependencyInjection;
using Quadra.Modules.Geo.DependencyInjection;
using Quadra.Modules.InGame.DependencyInjection;
using Quadra.Modules.Matches.DependencyInjection;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Profile.DependencyInjection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddMatchesModule(builder.Configuration);
builder.Services.AddInGameModule(builder.Configuration);
builder.Services.AddProfileModule(builder.Configuration);
builder.Services.AddGeoModule(builder.Configuration);
builder.Services.AddGamificationModule(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

// Opt-in (Database:MigrateOnStartup=true): apply pending EF migrations before serving traffic.
// Meant for the single-instance hosted environment, where nobody runs `dotnet ef` by hand.
// Leave it off when more than one instance can start at the same time.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.MigrateAsync();
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

await app.RunAsync();

public partial class Program;
