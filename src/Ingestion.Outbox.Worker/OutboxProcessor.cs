using System.Text.Json;
using Contracts;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestion.Outbox.Worker;

/// <summary>
/// Claims at most one bounded batch with PostgreSQL row locks. Locks remain held until
/// broker confirmation and the published timestamp are committed. Other workers skip
/// claimed rows; a crash rolls the transaction back, leaving rows eligible for retry.
/// </summary>
public sealed class OutboxProcessor(
    IngestionDbContext database,
    IOutboxMessagePublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger)
{
    private static readonly Action<ILogger, Guid, Exception?> PublishFailed =
        LoggerMessage.Define<Guid>(LogLevel.Warning, new EventId(6101, "OutboxPublishFailed"),
            "Outbox publication failed for message {MessageId}; record remains pending.");
    private static readonly Action<ILogger, Guid, Exception?> Published =
        LoggerMessage.Define<Guid>(LogLevel.Information, new EventId(6102, "OutboxPublished"),
            "Outbox message {MessageId} confirmed and marked published.");

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

            // Npgsql's FOR UPDATE SKIP LOCKED is safe across multiple worker instances.
            // No cache/distributed lock and no transaction across PostgreSQL and RabbitMQ.
            var messages = await database.OutboxMessages.FromSqlInterpolated(
                    $"SELECT * FROM outbox_messages WHERE published_at IS NULL ORDER BY occurred_at, \"Id\" LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);

            int published = 0;
            foreach (var row in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (row.EventType != nameof(ValueReceivedV1))
                    {
                        throw new InvalidOperationException($"Unsupported integration event type: {row.EventType}");
                    }

                    ValueReceivedV1? message = JsonSerializer.Deserialize<ValueReceivedV1>(row.Payload);
                    if (message is null || message.EventId != row.Id || message.ValueId != row.ValueId)
                    {
                        throw new InvalidOperationException("Outbox payload identity does not match its persisted record.");
                    }

                    // A timeout is treated as uncertain publication: leave the row pending.
                    // A later retry may publish a duplicate, handled by the future Inbox.
                    using var publishDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    publishDeadline.CancelAfter(options.Value.PublishTimeout);
                    await publisher.PublishAsync(message, row.Payload, publishDeadline.Token);

                    row.PublishedAt = clock.GetUtcNow();
                    published++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    PublishFailed(logger, row.Id, exception);
                    // Do not mark this row or spin on it within this batch.
                }
            }

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            foreach (var row in messages.Where(x => x.PublishedAt is not null))
            {
                Published(logger, row.Id, null);
            }

            return published;
        });
    }
}
