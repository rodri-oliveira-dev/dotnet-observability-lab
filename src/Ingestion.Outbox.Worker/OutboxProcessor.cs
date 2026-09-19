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
/// Permanently invalid records are quarantined in PostgreSQL and excluded from later polls.
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
    private static readonly Action<ILogger, Guid, string, Exception?> Quarantined =
        LoggerMessage.Define<Guid, string>(LogLevel.Error, new EventId(6104, "OutboxMessageQuarantined"),
            "Outbox message {MessageId} quarantined: {Reason}. Manual correction is required.");
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
                    $"SELECT * FROM outbox_messages WHERE published_at IS NULL AND quarantined_at IS NULL ORDER BY occurred_at, \"Id\" LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);

            int published = 0;
            foreach (var row in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (row.EventType != nameof(ValueReceivedV1))
                    {
                        Quarantine(row, "Unsupported event type");
                        continue;
                    }

                    ValueReceivedV1? message;
                    try
                    {
                        message = JsonSerializer.Deserialize<ValueReceivedV1>(row.Payload);
                    }
                    catch (JsonException)
                    {
                        Quarantine(row, "Malformed JSON payload");
                        continue;
                    }

                    if (message is null || message.EventId == Guid.Empty || message.ValueId == Guid.Empty ||
                        message.EventId != row.Id || message.ValueId != row.ValueId)
                    {
                        Quarantine(row, "Invalid event identity");
                        continue;
                    }

                    // This timeout is driven by the same TimeProvider as polling and timestamps.
                    // An uncertain broker confirmation leaves the row pending for possible duplicate retry.
                    using var timeout = new CancellationTokenSource(options.Value.PublishTimeout, clock);
                    using var publishDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
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

    private void Quarantine(OutboxMessage row, string reason)
    {
        row.QuarantinedAt = clock.GetUtcNow();
        row.QuarantineReason = reason;
        Quarantined(logger, row.Id, reason, null);
    }
}
