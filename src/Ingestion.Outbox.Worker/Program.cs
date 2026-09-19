using Ingestion.Outbox.Worker;
using Ingestion.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IngestionDbContext>("ingestion-db");
builder.AddRabbitMQClient("rabbitmq");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection(OutboxOptions.SectionName))
    .Validate(x => x.BatchSize is > 0 and <= 100, "Outbox batch size must be between 1 and 100.")
    .Validate(x => x.PollInterval >= TimeSpan.FromMilliseconds(100), "Outbox polling interval must be at least 100 ms.")
    .Validate(x => x.PublishTimeout >= TimeSpan.FromSeconds(1), "Outbox publish timeout must be at least 1 second.")
    .ValidateOnStart();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxMessagePublisher>();
builder.Services.AddHostedService<OutboxBackgroundService>();

var host = builder.Build();
host.Run();
