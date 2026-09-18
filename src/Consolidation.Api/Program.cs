var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "consolidation-api",
    status = "ready"
}));

app.Run();
