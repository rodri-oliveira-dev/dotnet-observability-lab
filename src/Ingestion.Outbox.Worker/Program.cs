using Ingestion.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IngestionDbContext>("ingestion-db");
builder.AddRabbitMQClient("rabbitmq");

var host = builder.Build();

host.Run();
