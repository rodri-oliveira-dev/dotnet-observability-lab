using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    public async Task Inbox_commit_and_duplicate_emit_distinct_low_cardinality_signals()
    {
        var measurements = new ConcurrentQueue<(string Name, double Value, string? Result, int TagCount)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Consolidation.Worker")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
        {
            var dimensions = tags.ToArray();
            measurements.Enqueue((instrument.Name, count,
                dimensions.FirstOrDefault(x => x.Key == "result").Value as string, dimensions.Length));
        });
        listener.SetMeasurementEventCallback<double>((instrument, seconds, tags, _) =>
        {
            var dimensions = tags.ToArray();
            measurements.Enqueue((instrument.Name, seconds,
                dimensions.FirstOrDefault(x => x.Key == "result").Value as string, dimensions.Length));
        });
        listener.Start();

        var activities = new ConcurrentQueue<(string? Name, string? Result)>();
        using var traces = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Consolidation.Worker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(
                (activity.DisplayName, activity.GetTagItem("consolidation.result") as string))
        };
        ActivitySource.AddActivityListener(traces);

        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
            await setup.Database.MigrateAsync();

        var message = new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 9m, DateTimeOffset.UtcNow);
        await using var db = new ConsolidationDbContext(options);
        var processor = new ConsolidationProcessor(db, TimeProvider.System);
        Assert.Equal(ConsolidationResult.Applied, await processor.ProcessAsync(message, CancellationToken.None));
        Assert.Equal(ConsolidationResult.Duplicate, await processor.ProcessAsync(message, CancellationToken.None));
        Assert.Contains(measurements, x => x is { Name: "lab.consolidation.values.processed", Value: 1, TagCount: 0 });
        Assert.Contains(measurements, x => x is { Name: "lab.consolidation.messages.duplicate", Value: 1, TagCount: 0 });
        Assert.Contains(measurements, x => x is { Name: "lab.consolidation.processing.duration", Result: "applied", TagCount: 1 });
        Assert.Contains(measurements, x => x is { Name: "lab.consolidation.processing.duration", Result: "duplicate", TagCount: 1 });
        Assert.Contains(activities, x => x is { Name: "consolidation.process_value", Result: "applied" });
        Assert.Contains(activities, x => x is { Name: "consolidation.process_value", Result: "duplicate" });
        Assert.All(measurements, x => Assert.True(x.TagCount <= 1));
    }

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
        using var producer = new Activity("producer");
        producer.SetIdFormat(ActivityIdFormat.W3C);
        producer.Start();
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
