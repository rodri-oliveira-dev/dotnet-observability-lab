using Consolidation.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConsolidationDbContext>("consolidation-db");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "consolidation-api",
    status = "ready"
}));

app.Run();
