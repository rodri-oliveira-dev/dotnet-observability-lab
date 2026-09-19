using Consolidation.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConsolidationDbContext>("consolidation-db");

var host = builder.Build();

host.Run();
