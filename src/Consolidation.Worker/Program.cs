using Consolidation.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConsolidationDbContext>("consolidation-db");
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddHostedService<RabbitMqTopologyInitializer>();

var host = builder.Build();

host.Run();
