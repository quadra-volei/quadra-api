using Quadra.Modules.Auth.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddAuthModule(builder.Configuration);

var app = builder.Build();

app.UseRouting();
app.UseAuthModule();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.MapControllers();

app.Run();

public partial class Program;
