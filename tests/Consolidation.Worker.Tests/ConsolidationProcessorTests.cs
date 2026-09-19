using System.Diagnostics;
using System.Text;
using Consolidation.Persistence;
using Consolidation.Worker;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
namespace Consolidation.Worker.Tests;
public sealed class ConsolidationProcessorTests
{
    [Fact]
    public async Task First_duplicate_concurrent_distinct_and_cacheless_deliveries_are_correct()
    {
        await using var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
            await setup.Database.MigrateAsync();
        var id = Guid.NewGuid();
        var first = new ValueReceivedV1(id, Guid.NewGuid(), 12.5m, DateTimeOffset.UtcNow);
        async Task<ConsolidationResult> Handle(ValueReceivedV1 value)
        {
            await using var db = new ConsolidationDbContext(options);
            return await new ConsolidationProcessor(db, TimeProvider.System).ProcessAsync(value, CancellationToken.None);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Handle(first)));
        Assert.Single(results, x => x == ConsolidationResult.Applied);
        Assert.Equal(7, results.Count(x => x == ConsolidationResult.Duplicate));
        Assert.Equal(ConsolidationResult.Applied,
            await Handle(new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 7.5m, DateTimeOffset.UtcNow)));
        await using var verify = new ConsolidationDbContext(options);
        var total = await verify.ConsolidatedTotals.SingleAsync();
        Assert.Equal(2, total.Count);
        Assert.Equal(20m, total.Sum);
        Assert.Equal(10m, total.Average);
        Assert.Equal(2, await verify.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task Rolled_back_aggregate_never_leaves_a_processed_inbox_entry()
    {
        await using var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
        {
            await setup.Database.MigrateAsync();
            // Force the second statement to fail after the Inbox insertion.
            await setup.Database.ExecuteSqlRawAsync("DROP TABLE consolidated_totals");
        }
        var message = new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 25m, DateTimeOffset.UtcNow);
        await using (var failing = new ConsolidationDbContext(options))
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new ConsolidationProcessor(failing, TimeProvider.System).ProcessAsync(message, CancellationToken.None));
        await using var verify = new ConsolidationDbContext(options);
        Assert.False(await verify.InboxMessages.AnyAsync(x => x.MessageId == message.MessageId));
    }

    [Fact]
    public void Consumer_extracts_W3C_headers_and_ignores_missing_or_invalid_carriers()
    {
        using var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        Assert.NotNull(producer);
        var valid = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes(producer.Id!)
        };
        var parent = ConsolidationConsumer.ExtractParent(valid);
        Assert.Equal(producer.TraceId, parent.TraceId);
        Assert.Equal(producer.SpanId, parent.SpanId);

        Assert.Equal(default, ConsolidationConsumer.ExtractParent(null));
        Assert.Equal(default, ConsolidationConsumer.ExtractParent(new Dictionary<string, object?>()));
        Assert.Equal(default, ConsolidationConsumer.ExtractParent(new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes("not-a-w3c-parent")
        }));
        Assert.Equal(default, ConsolidationConsumer.ExtractParent(new Dictionary<string, object?>
        {
            ["traceparent"] = new byte[] { 0xff, 0xfe }
        }));
    }

    [Fact]
    public void Missing_or_null_value_is_rejected_but_explicit_zero_is_valid()
    {
        var message = new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 0m, DateTimeOffset.UtcNow);
        var messageId = message.MessageId.ToString("D");
        var missingValue = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { message.EventId, message.ValueId, message.OccurredAt });
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ConsolidationConsumer.DeserializeAndValidate(missingValue, messageId));
        var nullValue = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { message.EventId, message.ValueId, Value = (decimal?)null, message.OccurredAt });
        Assert.Throws<System.Text.Json.JsonException>(() =>
            ConsolidationConsumer.DeserializeAndValidate(nullValue, messageId));
        var explicitZero = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message);
        Assert.Equal(0m, ConsolidationConsumer.DeserializeAndValidate(explicitZero, messageId).Value);
    }

    [Fact]
    public async Task Last_updated_at_never_regresses_when_older_update_commits_last()
    {
        await using var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
            await setup.Database.MigrateAsync();
        var later = new DateTimeOffset(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);
        var earlier = later.AddHours(-1);
        await using (var first = new ConsolidationDbContext(options))
        {
            await new ConsolidationProcessor(first, new FixedTimeProvider(later)).ProcessAsync(
                new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 10m, later), CancellationToken.None);
        }
        await using (var second = new ConsolidationDbContext(options))
        {
            await new ConsolidationProcessor(second, new FixedTimeProvider(earlier)).ProcessAsync(
                new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 20m, earlier), CancellationToken.None);
        }
        await using var verify = new ConsolidationDbContext(options);
        var total = await verify.ConsolidatedTotals.SingleAsync();
        Assert.Equal(2, total.Count);
        Assert.Equal(30m, total.Sum);
        Assert.Equal(later, total.LastUpdatedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
