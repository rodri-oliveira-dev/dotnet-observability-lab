using Consolidation.Api.Consolidated;
using Consolidation.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ConsolidationDbContext>("consolidation-db");
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ConsolidatedQuery>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "consolidation-api",
    status = "ready"
}));

app.MapGet("/consolidated", async (ConsolidatedQuery query, CancellationToken cancellationToken) =>
    TypedResults.Ok(await query.GetAsync(cancellationToken)));

app.Run();

// Enables HTTP integration tests without exposing application internals.
public partial class Program { }
