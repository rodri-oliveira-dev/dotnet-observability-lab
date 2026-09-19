using System.Collections.Concurrent;
using System.Text.Json;
using Contracts;
using Ingestion.Outbox.Worker;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
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
        await using var database = fixture.CreateContext();
        var processor = CreateProcessor(database, publisher);

        Assert.Equal(0, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Null((await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id)).PublishedAt);

        publisher.Fail = false;
        Assert.Equal(1, await processor.ProcessBatchAsync(CancellationToken.None));
        Assert.Equal([id], publisher.Ids);
        Assert.NotNull((await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id)).PublishedAt);
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
        Assert.Null((await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == bad)).PublishedAt);
        Assert.NotNull((await database.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == good)).PublishedAt);
    }

    private async Task<Guid> SeedAsync(bool published = false, string eventType = nameof(ValueReceivedV1))
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
            Payload = JsonSerializer.Serialize(message),
            OccurredAt = now,
            PublishedAt = published ? now : null
        });
        await database.SaveChangesAsync();
        return message.EventId;
    }

    private static OutboxProcessor CreateProcessor(IngestionDbContext db, IOutboxMessagePublisher publisher) =>
        new(db, publisher, TimeProvider.System, Options.Create(new OutboxOptions()),
            NullLogger<OutboxProcessor>.Instance);

    private sealed class RecordingPublisher : IOutboxMessagePublisher
    {
        private readonly ConcurrentQueue<Guid> _ids = new();
        public bool Fail { get; set; }
        public TimeSpan Delay { get; set; }
        public Guid[] Ids => _ids.ToArray();

        public async Task PublishAsync(ValueReceivedV1 message, string json, CancellationToken cancellationToken)
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            if (Fail)
            {
                throw new InvalidOperationException("Simulated broker failure.");
            }

            _ids.Enqueue(message.EventId);
        }
    }
}
