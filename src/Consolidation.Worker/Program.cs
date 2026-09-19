using Consolidation.Persistence;
using Consolidation.Worker;
using Microsoft.EntityFrameworkCore;
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConsolidationDbContext>("consolidation-db");
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ConsolidationProcessor>();
builder.Services.AddHostedService<ConsolidationConsumer>();
var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
    await db.Database.MigrateAsync();
}
await host.RunAsync();
