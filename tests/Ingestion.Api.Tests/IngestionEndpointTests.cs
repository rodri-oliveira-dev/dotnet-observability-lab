using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Contracts;
using Ingestion.Api.Values;
using Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ingestion.Api.Tests;

public sealed class IngestionDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("ingestion_db")
        .WithUsername("postgres")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Aspire resolves this setting while Program registers its pooled DbContext.
        // WebApplicationFactory's later ConfigureAppConfiguration hook is too late.
        Environment.SetEnvironmentVariable("ConnectionStrings__ingestion-db", _postgres.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__ingestion-db", null);
        await _postgres.DisposeAsync();
    }

    public IngestionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IngestionDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);

    public WebApplicationFactory<Program> CreateFactory(bool cacheUnavailable = false, TimeProvider? clock = null) =>
        new IngestionWebApplicationFactory(cacheUnavailable, clock);

    private sealed class IngestionWebApplicationFactory(
        bool cacheUnavailable, TimeProvider? clock) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDistributedCache>();
                if (cacheUnavailable)
                {
                    services.AddSingleton<IDistributedCache, UnavailableCache>();
                }
                else
                {
                    services.AddDistributedMemoryCache();
                }

                if (clock is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(clock);
                }
            });
        }
    }

    private sealed class UnavailableCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
        public void Refresh(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
        public void Remove(string key) => throw new InvalidOperationException("Redis unavailable.");
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("Redis unavailable.");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("Redis unavailable.");
    }
}

public sealed class IngestionEndpointTests(IngestionDatabaseFixture fixture)
    : IClassFixture<IngestionDatabaseFixture>
{
    [Fact]
    public async Task New_value_commits_one_value_and_corresponding_outbox_event()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        using var factory = fixture.CreateFactory(clock: new FixedTimeProvider(now));
        using var client = factory.CreateClient();
        string key = NewKey();

        using var response = await PostAsync(client, key, 10.5m);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<ValueReceipt>();
        Assert.NotNull(receipt);
        Assert.Equal(10.5m, receipt.Value);

        await using var database = fixture.CreateContext();
        var received = await database.ReceivedValues.SingleAsync(x => x.IdempotencyKey == key);
        var outbox = await database.OutboxMessages.SingleAsync(x => x.ValueId == receipt.Id);
        var payload = JsonSerializer.Deserialize<ValueReceivedV1>(outbox.Payload);

        Assert.Equal(receipt.Id, received.Id);
        Assert.Equal(now, received.ReceivedAt);
        Assert.Equal(now, outbox.OccurredAt);
        Assert.Null(outbox.PublishedAt);
        Assert.Equal("ValueReceivedV1", outbox.EventType);
        Assert.NotNull(payload);
        Assert.Equal(outbox.Id, payload.EventId);
        Assert.Equal(receipt.Id, payload.ValueId);
        Assert.Equal(10.5m, payload.Value);
    }

    [Fact]
    public async Task Missing_key_or_value_returns_validation_problem_and_persists_nothing()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        string key = NewKey();

        using var noKey = await client.PostAsJsonAsync("/values", new { value = 10m });
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal("application/problem+json", noKey.Content.Headers.ContentType?.MediaType);

        using var missingValue = await PostAsync(client, key, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingValue.StatusCode);
        Assert.Equal("application/problem+json", missingValue.Content.Headers.ContentType?.MediaType);

        await using var database = fixture.CreateContext();
        Assert.Equal(0, await database.ReceivedValues.CountAsync(x => x.IdempotencyKey == key));
    }

    [Fact]
    public async Task Same_key_and_equivalent_numeric_payload_replays_the_original_receipt()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        string key = NewKey();

        using var first = await PostAsync(client, key, 10.50m);
        using var repeated = await PostAsync(client, key, 10.5m);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var initial = await first.Content.ReadFromJsonAsync<ValueReceipt>();
        var replay = await repeated.Content.ReadFromJsonAsync<ValueReceipt>();
        Assert.Equal(initial, replay);

        await AssertSingleOperationAsync(fixture, key);
    }

    [Fact]
    public async Task Reusing_key_with_different_value_returns_conflict_without_side_effects()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        string key = NewKey();

        using var initial = await PostAsync(client, key, 10.5m);
        using var conflicting = await PostAsync(client, key, 11.5m);
        Assert.Equal(HttpStatusCode.Created, initial.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Equal("application/problem+json", conflicting.Content.Headers.ContentType?.MediaType);

        await AssertSingleOperationAsync(fixture, key);
    }

    [Fact]
    public async Task Concurrent_same_key_creates_exactly_one_value_and_outbox_message()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        string key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => PostAsync(client, key, 7.25m)));
        try
        {
            Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
            Assert.Equal(11, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
            var receipts = await Task.WhenAll(responses.Select(x =>
                x.Content.ReadFromJsonAsync<ValueReceipt>()));
            Assert.Single(receipts.Select(x => x!.Id).Distinct());
            await AssertSingleOperationAsync(fixture, key);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Redis_unavailability_does_not_affect_durable_idempotency()
    {
        using var factory = fixture.CreateFactory(cacheUnavailable: true);
        using var client = factory.CreateClient();
        string key = NewKey();

        using var first = await PostAsync(client, key, 8m);
        using var replay = await PostAsync(client, key, 8m);
        using var conflict = await PostAsync(client, key, 9m);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<ValueReceipt>())?.Id,
            (await replay.Content.ReadFromJsonAsync<ValueReceipt>())?.Id);

        await AssertSingleOperationAsync(fixture, key);
    }

    [Fact]
    public async Task Outbox_insert_failure_rolls_back_business_value()
    {
        using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        // Start the host (and apply its migrations) before deliberately rejecting Outbox inserts.
        using var health = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        string key = NewKey();

        await using var database = fixture.CreateContext();
        int outboxCountBefore = await database.OutboxMessages.CountAsync();
        await database.Database.ExecuteSqlRawAsync(
            "ALTER TABLE outbox_messages ADD CONSTRAINT reject_outbox_atomicity_test CHECK (event_type <> 'ValueReceivedV1') NOT VALID");
        try
        {
            using var response = await PostAsync(client, key, 3m);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(0, await database.ReceivedValues.CountAsync(x => x.IdempotencyKey == key));
            Assert.Equal(outboxCountBefore, await database.OutboxMessages.CountAsync());
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync(
                "ALTER TABLE outbox_messages DROP CONSTRAINT reject_outbox_atomicity_test");
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string key, decimal? value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/values")
        {
            Content = JsonContent.Create(new { value })
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task AssertSingleOperationAsync(IngestionDatabaseFixture fixture, string key)
    {
        await using var database = fixture.CreateContext();
        var values = await database.ReceivedValues.Where(x => x.IdempotencyKey == key).ToListAsync();
        Assert.Single(values);
        Assert.Equal(1, await database.OutboxMessages.CountAsync(x => x.ValueId == values[0].Id));
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
