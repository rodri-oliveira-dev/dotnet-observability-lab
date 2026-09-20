using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using Contracts;
using Ingestion.Outbox.Worker;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ingestion.Outbox.Worker.Tests;

public sealed class OutboxDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("ingestion_db")
        .WithUsername("postgres")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _database.DisposeAsync().AsTask();

    public IngestionDbContext CreateContext() => new(
        new DbContextOptionsBuilder<IngestionDbContext>()
            .UseNpgsql(_database.GetConnectionString()).Options);
}

public sealed class OutboxPublisherTests(OutboxDatabaseFixture fixture)
    : IClassFixture<OutboxDatabaseFixture>
{

    [Fact]
    public async Task Hosted_worker_logs_scope_resolution_failure_and_stops_cleanly()
    {
        // A transient scope/resolution failure must not crash the hosted service.
        // Cancellation while waiting for the next poll must stop it promptly.
        using var services = new ServiceCollection().BuildServiceProvider();
        var logger = new RecordingWorkerLogger();
        using var worker = new OutboxBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new OutboxOptions { PollInterval = TimeSpan.FromHours(1) }),
            logger);
        await worker.StartAsync(CancellationToken.None);
        var eventId = await logger.Failure.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(6103, eventId.Id);
        Assert.IsType<InvalidOperationException>(eventId.Exception);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Publisher_omits_trace_headers_without_valid_W3C_context_and_keeps_trace_state()
    {
        Assert.Null(RabbitMqOutboxMessagePublisher.BuildTraceHeaders(null));
        using var hierarchical = new Activity("legacy");
        hierarchical.SetIdFormat(ActivityIdFormat.Hierarchical);
        hierarchical.Start();
        Assert.Null(RabbitMqOutboxMessagePublisher.BuildTraceHeaders(hierarchical));
        hierarchical.Stop();

        using var producer = new Activity("publisher");
        producer.SetIdFormat(ActivityIdFormat.W3C);
        producer.TraceStateString = "vendor=value";
        producer.Start();
        var headers = RabbitMqOutboxMessagePublisher.BuildTraceHeaders(producer);
        Assert.NotNull(headers);
        Assert.Equal(producer.Id, Encoding.UTF8.GetString(Assert.IsType<byte[]>(headers["traceparent"])));
        Assert.Equal("vendor=value", Encoding.UTF8.GetString(Assert.IsType<byte[]>(headers["tracestate"])));
    }

    [Fact]
    public void Publisher_encodes_W3C_producer_context_in_RabbitMQ_headers()
    {
        Assert.Null(RabbitMqOutboxMessagePublisher.BuildTraceHeaders(null));

        using var producer = new Activity("producer");
        producer.SetIdFormat(ActivityIdFormat.W3C);
        producer.Start();
        var headers = RabbitMqOutboxMessagePublisher.BuildTraceHeaders(producer);
        Assert.NotNull(headers);
        Assert.Equal(producer.Id, Encoding.UTF8.GetString(Assert.IsType<byte[]>(headers["traceparent"])));
        Assert.True(ActivityContext.TryParse(
            Encoding.UTF8.GetString(Assert.IsType<byte[]>(headers["traceparent"])), null, out var extracted));
        Assert.Equal(producer.TraceId, extracted.TraceId);
        Assert.Equal(producer.SpanId, extracted.SpanId);
    }

    [Fact]
    public async Task Publisher_uses_persisted_request_parent_not_the_worker_polling_context()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Ingestion.Outbox.Worker",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var origin = new Activity("originating-http");
        origin.SetIdFormat(ActivityIdFormat.W3C);
        origin.Start();
        Assert.NotNull(origin);
        string traceParent = origin.Id!;
        var expectedTraceId = origin.TraceId;
        var expectedParentSpanId = origin.SpanId;
        origin.Stop();

        Guid messageId = await SeedAsync(traceParent: traceParent);
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();
        Assert.Equal(1, await CreateProcessor(database, publisher).ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([messageId], publisher.Ids);
        Assert.NotNull(publisher.ObservedActivity);
        Assert.Equal(ActivityKind.Producer, publisher.ObservedActivity.Kind);
        Assert.Equal(expectedTraceId, publisher.ObservedActivity.TraceId);
        Assert.Equal(expectedParentSpanId, publisher.ObservedActivity.ParentSpanId);
    }

    [Fact]
    public async Task Failed_then_confirmed_publication_emits_failure_success_and_full_backlog_metrics()
    {
        var measurements = new ConcurrentQueue<(string Name, long Count, int TagCount)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Ingestion.Outbox.Worker")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
            measurements.Enqueue((instrument.Name, count, tags.Length)));
        listener.Start();

        Guid id = await SeedAsync();
        var clock = new ManualTimeProvider();
        var publisher = new RecordingPublisher { Fail = true };
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher, clock);
        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Contains(measurements, x => x is { Name: "lab.outbox.messages.publish_failures", Count: 1 });
        Assert.Contains(measurements, x => x.Name == "lab.outbox.messages.pending" && x.Count >= 1);

        publisher.Fail = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([id], publisher.Ids);
        Assert.Contains(measurements, x => x is { Name: "lab.outbox.messages.published", Count: 1 });
        long pending = await database.OutboxMessages.AsNoTracking().LongCountAsync(
            x => x.PublishedAt == null && x.QuarantinedAt == null);
        Assert.Equal(pending, measurements.Last(x => x.Name == "lab.outbox.messages.pending").Count);
        Assert.All(measurements, x => Assert.Equal(0, x.TagCount));
    }

    [Fact]
    public async Task Rolled_back_retry_does_not_emit_a_failure_metric_or_log()
    {
        var failures = new ConcurrentQueue<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Ingestion.Outbox.Worker" &&
                instrument.Name == "lab.outbox.messages.publish_failures")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => failures.Enqueue(value));
        listener.Start();

        Guid id = await SeedAsync();
        // NOT VALID does not inspect rows from other test cases, but every new UPDATE
        // must pass the constraint. This forces SaveChanges to reject this retry.
        await using (var setup = fixture.CreateContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE outbox_messages ADD CONSTRAINT ck_outbox_retry_telemetry_rollback " +
                "CHECK (next_attempt_at IS NULL) NOT VALID");
        }

        var publisher = new RecordingPublisher { Fail = true };
        var logger = new RecordingOutboxLogger();
        try
        {
            await using (var failingDb = fixture.CreateContext())
            {
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    new OutboxProcessor(failingDb, publisher, TimeProvider.System,
                        Options.Create(new OutboxOptions()), logger)
                    .ProcessBatchAsync(CancellationToken.None));
            }

            await using var verify = fixture.CreateContext();
            var row = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(0, row.PublishAttempts);
            Assert.Null(row.NextAttemptAt);
            Assert.Null(row.PublishedAt);
            Assert.Empty(failures);
            Assert.DoesNotContain(6101, logger.EventIds);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync(
                "ALTER TABLE outbox_messages DROP CONSTRAINT ck_outbox_retry_telemetry_rollback");
        }

        // After the constraint is removed, the identical publish failure can commit.
        await using (var resumed = fixture.CreateContext())
        {
            Assert.Equal(0, await new OutboxProcessor(resumed, publisher, TimeProvider.System,
                Options.Create(new OutboxOptions()), logger)
                .ProcessBatchAsync(CancellationToken.None));
        }
        Assert.Equal([1L], failures);
        Assert.Equal(1, logger.EventIds.Count(x => x == 6101));
    }

    [Fact]
    public async Task Pending_message_is_published_then_marked_only_after_confirmation()
    {
        Guid id = await SeedAsync();
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher);

        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([id], publisher.Ids);
        Assert.NotNull((await database.OutboxMessages.SingleAsync(x => x.Id == id)).PublishedAt);
    }

    [Fact]
    public async Task Failed_publication_remains_pending_and_can_be_retried()
    {
        Guid id = await SeedAsync();
        var publisher = new RecordingPublisher { Fail = true };
        var clock = new ManualTimeProvider();
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher, clock);

        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        var pending = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Null(pending.PublishedAt);
        Assert.Null(pending.QuarantinedAt);
        Assert.Equal(1, pending.PublishAttempts);
        Assert.Equal(clock.GetUtcNow().AddSeconds(5), pending.NextAttemptAt);

        publisher.Fail = false;
        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal(1, publisher.Attempts);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([id], publisher.Ids);
        var published = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.NotNull(published.PublishedAt);
        Assert.Null(published.NextAttemptAt);
    }

    [Fact]
    public async Task Already_published_message_is_not_republished()
    {
        Guid id = await SeedAsync(published: true);
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();

        Assert.Equal(0, await CreateProcessor(database, publisher).ProcessBatchAsync(CancellationToken.None));
        Assert.DoesNotContain(id, publisher.Ids);
    }

    [Fact]
    public async Task Two_concurrent_workers_skip_each_others_locked_rows()
    {
        Guid id = await SeedAsync();
        var publisher = new RecordingPublisher { Delay = TimeSpan.FromMilliseconds(150) };
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var results = await Task.WhenAll(
            CreateProcessor(first, publisher).ProcessBatchAsync(CancellationToken.None),
            CreateProcessor(second, publisher).ProcessBatchAsync(CancellationToken.None));

        Assert.Equal(1, results.Sum());
        Assert.Equal([id], publisher.Ids);
    }

    [Fact]
    public async Task A_failure_for_one_row_does_not_prevent_other_rows_in_the_batch()
    {
        Guid bad = await SeedAsync(eventType: "Unsupported.v1");
        Guid good = await SeedAsync();
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();

        Assert.Equal(1, await CreateProcessor(database, publisher).ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([good], publisher.Ids);
        var invalid = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == bad);
        Assert.Null(invalid.PublishedAt);
        Assert.NotNull(invalid.QuarantinedAt);
        Assert.Equal("Unsupported event type", invalid.QuarantineReason);
        Assert.NotNull((await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == good)).PublishedAt);
    }

    [Fact]
    public async Task A_full_batch_of_poison_rows_is_quarantined_and_cannot_starve_later_valid_events()
    {
        var earlier = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var poisonIds = new List<Guid>();
        for (int i = 0; i < 10; i++)
        {
            poisonIds.Add(await SeedAsync(eventType: "Unknown.v1", occurredAt: earlier));
        }

        Guid validId = await SeedAsync();
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher);

        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Empty(publisher.Ids);
        Assert.Equal(10, await database.OutboxMessages.AsNoTracking()
            .CountAsync(x => poisonIds.Contains(x.Id) && x.QuarantinedAt != null && x.PublishedAt == null));

        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([validId], publisher.Ids);
        Assert.NotNull((await database.OutboxMessages.AsNoTracking()
            .SingleAsync(x => x.Id == validId)).PublishedAt);
        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_json_and_mismatched_identity_are_quarantined_without_publishing()
    {
        var earlier = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        Guid malformed = await SeedAsync(payload: "{invalid json", occurredAt: earlier);
        Guid wrongIdentity = await SeedAsync(payload: JsonSerializer.Serialize(
            new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 5m, earlier)), occurredAt: earlier);
        Guid good = await SeedAsync();
        var publisher = new RecordingPublisher();
        await using var database = fixture.CreateContext();

        Assert.Equal(1, await CreateProcessor(database, publisher).ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([good], publisher.Ids);
        var malformedRow = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == malformed);
        var wrongRow = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == wrongIdentity);
        Assert.Null(malformedRow.PublishedAt);
        Assert.Equal("Malformed JSON payload", malformedRow.QuarantineReason);
        Assert.NotNull(malformedRow.QuarantinedAt);
        Assert.Null(wrongRow.PublishedAt);
        Assert.Equal("Invalid event identity", wrongRow.QuarantineReason);
        Assert.NotNull(wrongRow.QuarantinedAt);
    }

    [Fact]
    public async Task Publish_timeout_uses_injected_time_provider_and_keeps_message_retryable()
    {
        Guid id = await SeedAsync();
        var clock = new ManualTimeProvider();
        var publisher = new WaitingPublisher();
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher, clock);
        Task<int> result = processor.ProcessBatchAsync(CancellationToken.None);

        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await result.WaitAsync(TimeSpan.FromSeconds(10)));

        var pending = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Null(pending.PublishedAt);
        Assert.Null(pending.QuarantinedAt);
        Assert.Null(pending.QuarantineReason);
        Assert.Equal(1, pending.PublishAttempts);
        Assert.Equal(clock.GetUtcNow().AddSeconds(5), pending.NextAttemptAt);

        // The timeout is transient but must honor its persisted retry deadline.
        var retryPublisher = new RecordingPublisher();
        Assert.Equal(0, await CreateProcessor(database, retryPublisher, clock)
            .ProcessBatchAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await CreateProcessor(database, retryPublisher, clock)
            .ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([id], retryPublisher.Ids);
    }

    [Fact]
    public async Task A_full_batch_of_transient_failures_does_not_starve_a_later_valid_message()
    {
        var earlier = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var failingIds = new List<Guid>();
        for (int i = 0; i < 10; i++)
        {
            failingIds.Add(await SeedAsync(occurredAt: earlier));
        }

        Guid laterId = await SeedAsync();
        var clock = new ManualTimeProvider();
        var publisher = new RecordingPublisher { Fail = true };
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher, clock);

        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal(10, publisher.Attempts);
        Assert.Equal(10, await database.OutboxMessages.AsNoTracking().CountAsync(x =>
            failingIds.Contains(x.Id) && x.PublishedAt == null && x.QuarantinedAt == null &&
            x.PublishAttempts == 1 && x.NextAttemptAt == clock.GetUtcNow().AddSeconds(5)));

        publisher.Fail = false;
        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([laterId], publisher.Ids);
        Assert.Equal(11, publisher.Attempts);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(10, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal(11, publisher.Ids.Length);
        Assert.Equal(0, await database.OutboxMessages.AsNoTracking()
            .CountAsync(x => failingIds.Contains(x.Id) && x.PublishedAt == null));
    }

    [Fact]
    public async Task Retry_backoff_grows_is_bounded_and_survives_worker_restart()
    {
        Guid id = await SeedAsync();
        var clock = new ManualTimeProvider();
        var publisher = new RecordingPublisher { Fail = true };

        for (int attempt = 1; attempt <= 9; attempt++)
        {
            // A new DbContext/processor models a restarted worker with durable retry state.
            await using var database = fixture.CreateContext();
            var processor = CreateProcessor(database, publisher, clock);
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));

            var pending = await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
            var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * (1 << Math.Min(attempt - 1, 6))));
            Assert.Equal(attempt, pending.PublishAttempts);
            Assert.Equal(clock.GetUtcNow().Add(delay), pending.NextAttemptAt);
            Assert.Null(pending.QuarantinedAt);
            Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
            Assert.Equal(attempt, publisher.Attempts);
            clock.Advance(delay);
        }

        publisher.Fail = false;
        await using var resumedDatabase = fixture.CreateContext();
        Assert.Equal(1, await CreateProcessor(resumedDatabase, publisher, clock)
            .ProcessBatchAsync(CancellationToken.None));
        var published = await resumedDatabase.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.NotNull(published.PublishedAt);
        Assert.Null(published.NextAttemptAt);
        Assert.Equal(9, published.PublishAttempts);
    }

    private async Task<Guid> SeedAsync(bool published = false, string eventType = nameof(ValueReceivedV1),
        string? payload = null, DateTimeOffset? occurredAt = null, string? traceParent = null)
    {
        await using var database = fixture.CreateContext();
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var value = new ReceivedValue
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestFingerprint = new string('A', 64),
            Value = 10.5m,
            ReceivedAt = now
        };
        var message = new ValueReceivedV1(Guid.NewGuid(), value.Id, value.Value, now);
        database.ReceivedValues.Add(value);
        database.OutboxMessages.Add(new OutboxMessage
        {
            Id = message.EventId,
            ValueId = value.Id,
            EventType = eventType,
            Payload = payload ?? JsonSerializer.Serialize(message),
            OccurredAt = occurredAt ?? now,
            PublishedAt = published ? now : null,
            TraceParent = traceParent
        });
        await database.SaveChangesAsync();
        return message.EventId;
    }

    private static OutboxProcessor CreateProcessor(IngestionDbContext db, IOutboxMessagePublisher publisher,
        TimeProvider? clock = null) =>
        new(db, publisher, clock ?? TimeProvider.System, Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

    private sealed class RecordingWorkerLogger : ILogger<OutboxBackgroundService>
    {
        public TaskCompletionSource<(int Id, Exception Exception)> Failure { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null)
                Failure.TrySetResult((eventId.Id, exception));
        }
    }

    private sealed class RecordingOutboxLogger : ILogger<OutboxProcessor>
    {
        private readonly ConcurrentQueue<int> _eventIds = new();
        public int[] EventIds => _eventIds.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _eventIds.Enqueue(eventId.Id);
        }
    }

    private sealed class RecordingPublisher : IOutboxMessagePublisher
    {
        private readonly ConcurrentQueue<Guid> _ids = new();
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public bool Fail { get; set; }
        public TimeSpan Delay { get; set; }
        public Guid[] Ids => _ids.ToArray();
        public Activity? ObservedActivity { get; private set; }

        public async Task PublishAsync(ValueReceivedV1 message, string json, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Fail)
            {
                throw new InvalidOperationException("Simulated broker failure.");
            }

            ObservedActivity = Activity.Current;
            _ids.Enqueue(message.EventId);
        }
    }
    private sealed class WaitingPublisher : IOutboxMessagePublisher
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync(ValueReceivedV1 message, string json, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    // Minimal fake clock: cancellation deadlines fire only when this provider is advanced.
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            foreach (var timer in _timers.ToArray())
            {
                timer.FireIfDue(_now);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private readonly object _sync = new();
            private DateTimeOffset? _due;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                    return true;
                }
            }

            public void FireIfDue(DateTimeOffset now)
            {
                lock (_sync)
                {
                    if (_disposed || _due is null || _due > now)
                    {
                        return;
                    }

                    _due = null;
                }

                callback(state);
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _disposed = true;
                    _due = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
