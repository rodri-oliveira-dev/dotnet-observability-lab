using Ingestion.Api.Values;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IngestionDbContext>("ingestion-db");
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IngestValueHandler>();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("redis");
    options.InstanceName = "observability-lab:";
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "ingestion-api",
    status = "ready"
}));

app.MapPost("/values", async (HttpContext context, IngestValueRequest request,
    IngestValueHandler handler, CancellationToken cancellationToken) =>
{
    var keyHeader = context.Request.Headers["Idempotency-Key"];
    if (keyHeader.Count != 1 || string.IsNullOrWhiteSpace(keyHeader[0]) ||
        keyHeader[0]!.Length > 200 || keyHeader[0] != keyHeader[0]!.Trim() ||
        keyHeader[0]!.Any(char.IsControl))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Idempotency-Key"] = ["A single nonempty key of at most 200 characters is required."]
        });
    }

    if (request.Value is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["value"] = ["A decimal value is required."]
        });
    }

    var result = await handler.HandleAsync(keyHeader[0]!, request.Value.Value, cancellationToken);
    if (result.State == IngestValueState.Conflict)
    {
        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
            title: "Idempotency key conflict",
            detail: "This Idempotency-Key was already used with a different value.");
    }

    return result.State == IngestValueState.Created
        ? Results.Json(result.Receipt, statusCode: StatusCodes.Status201Created)
        : Results.Ok(result.Receipt);
});

// Only the write API migrates ingestion_db; its future worker never competes for DDL.
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
    await database.Database.MigrateAsync();
}

app.Run();

public partial class Program;
