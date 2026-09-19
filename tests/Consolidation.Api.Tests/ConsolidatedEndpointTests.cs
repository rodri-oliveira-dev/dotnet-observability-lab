using System.Net;
using System.Net.Http.Json;
using Consolidation.Api.Consolidated;
using Consolidation.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Consolidation.Api.Tests;

/// <summary>Runs the read API against its real PostgreSQL read model without starting ingestion or RabbitMQ.</summary>
public sealed class ConsolidationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("consolidation_db")
        .WithUsername("postgres")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        // The Aspire connection is read during service registration, before the WebApplicationFactory hooks.
        Environment.SetEnvironmentVariable("ConnectionStrings__consolidation-db", postgres.GetConnectionString());
        // Deliberately unusable: the API must never open an ingestion database connection.
        Environment.SetEnvironmentVariable("ConnectionStrings__ingestion-db", "Host=127.0.0.1;Port=1;Database=ingestion_db");
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__consolidation-db", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__ingestion-db", null);
        await postgres.DisposeAsync();
    }

    public ConsolidationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);

    public WebApplicationFactory<Program> CreateFactory() => new();
}

public sealed class ConsolidatedEndpointTests(ConsolidationDatabaseFixture fixture)
    : IClassFixture<ConsolidationDatabaseFixture>
{
    [Fact]
    public async Task Empty_database_returns_stable_zero_snapshot()
    {
        // xUnit does not guarantee test method order; reset the shared fixture's read row.
        await using (var database = fixture.CreateContext())
            await database.ConsolidatedTotals.ExecuteDeleteAsync();

        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/consolidated");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<ConsolidatedResponse>();
        Assert.Equal(new ConsolidatedResponse(0, 0m, 0m, null), snapshot);
    }

    [Fact]
    public async Task Persisted_read_model_is_returned_without_ingestion_or_broker()
    {
        var updated = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);
        await using (var database = fixture.CreateContext())
        {
            database.ConsolidatedTotals.Add(new ConsolidatedTotal
            {
                Id = 1, Count = 3, Sum = 60m, LastUpdatedAt = updated
            });
            await database.SaveChangesAsync();
        }

        // Only the read API and consolidation_db are started; no ingestion process or RabbitMQ exists.
        using (var factory = fixture.CreateFactory())
        using (var client = factory.CreateClient())
        {
            using var response = await client.GetAsync("/consolidated");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new ConsolidatedResponse(3, 60m, 20m, updated),
                await response.Content.ReadFromJsonAsync<ConsolidatedResponse>());
        }

        // Restarting the read API must still return the last committed result with upstream offline.
        using (var factory = fixture.CreateFactory())
        using (var client = factory.CreateClient())
        {
            Assert.Equal(new ConsolidatedResponse(3, 60m, 20m, updated),
                await client.GetFromJsonAsync<ConsolidatedResponse>("/consolidated"));
        }
    }
}
