using Quadra.Workers.Background;
using Quadra.Workers.Background.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddProfileEventConsumers(builder.Configuration);

var host = builder.Build();
host.Run();
