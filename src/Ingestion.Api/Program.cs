using Ingestion.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IngestionDbContext>("ingestion-db");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "ingestion-api",
    status = "ready"
}));

app.Run();
