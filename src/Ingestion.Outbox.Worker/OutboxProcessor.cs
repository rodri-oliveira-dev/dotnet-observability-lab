using System.Diagnostics;
using System.Text.Json;
using Contracts;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Messaging;

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
    private static readonly ActivitySource Traces = new("Ingestion.Outbox.Worker");

    private static readonly Action<ILogger, Guid, Guid, DateTimeOffset, Exception?> PublishFailed =
        LoggerMessage.Define<Guid, Guid, DateTimeOffset>(LogLevel.Warning,
            new EventId(6101, "OutboxPublishFailed"),
            "Outbox publish attempt failed for message {MessageId}, value {ValueId}; next retry at {NextAttemptAt}.");
    private static readonly Action<ILogger, Guid, Guid, string, Exception?> Quarantined =
        LoggerMessage.Define<Guid, Guid, string>(LogLevel.Error, new EventId(6104, "OutboxMessageQuarantined"),
            "Outbox message {MessageId} for value {ValueId} quarantined: {Reason}. Manual correction is required.");
    private static readonly Action<ILogger, Guid, Guid, Exception?> Published =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, new EventId(6102, "OutboxPublished"),
            "Outbox message {MessageId} for value {ValueId} confirmed and marked published.");
    private static readonly Action<ILogger, Exception?> PendingCountFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(6105, "OutboxBacklogMeasurementFailed"),
            "Outbox backlog measurement unavailable; publishing is unaffected.");

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var strategy = database.Database.CreateExecutionStrategy();
        var batch = await strategy.ExecuteAsync(async () =>
        {
            // Each execution-strategy attempt owns its own notifications. A rolled-back
            // attempt must never report a durable publication or scheduled retry.
            List<PublishFailure> failures = [];
            List<QuarantinedMessage> quarantined = [];
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

            // Npgsql's FOR UPDATE SKIP LOCKED is safe across multiple worker instances.
            // No cache/distributed lock and no transaction across PostgreSQL and RabbitMQ.
            var now = clock.GetUtcNow();
            var messages = await database.OutboxMessages.FromSqlInterpolated(
                    $"SELECT * FROM outbox_messages WHERE published_at IS NULL AND quarantined_at IS NULL AND (next_attempt_at IS NULL OR next_attempt_at <= {now}) ORDER BY occurred_at, \"Id\" LIMIT {options.Value.BatchSize} FOR UPDATE SKIP LOCKED")
                .ToListAsync(cancellationToken);

            int published = 0;
            foreach (var row in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (row.EventType != nameof(ValueReceivedV1))
                    {
                        Quarantine(row, "Unsupported event type", quarantined);
                        continue;
                    }

                    ValueReceivedV1? message;
                    try
                    {
                        message = JsonSerializer.Deserialize<ValueReceivedV1>(row.Payload);
                    }
                    catch (JsonException)
                    {
                        Quarantine(row, "Malformed JSON payload", quarantined);
                        continue;
                    }

                    if (message is null || message.EventId == Guid.Empty || message.ValueId == Guid.Empty ||
                        message.EventId != row.Id || message.ValueId != row.ValueId)
                    {
                        Quarantine(row, "Invalid event identity", quarantined);
                        continue;
                    }

                    // Restore the persisted request parent even when publication happens hours later.
                    // Missing/invalid legacy context starts a new trace; publication never depends on telemetry.
                    var hasParent = ActivityContext.TryParse(row.TraceParent, row.TraceState, out var parent);
                    using var dispatch = Traces.StartActivity("rabbitmq publish ValueReceived.v1",
                        ActivityKind.Producer, hasParent ? parent : default);
                    dispatch?.SetTag("messaging.system", "rabbitmq");
                    dispatch?.SetTag("messaging.destination.name", RabbitMqTopology.Exchange);
                    dispatch?.SetTag("messaging.operation.name", "publish");
                    dispatch?.SetTag("messaging.message.id", message.MessageId.ToString("D"));
                    dispatch?.SetTag("messaging.message.conversation_id", message.CorrelationId);
                    dispatch?.SetTag("lab.value.id", message.ValueId.ToString("D"));

                    // This timeout is driven by the same TimeProvider as polling and timestamps.
                    // An uncertain broker confirmation leaves the row pending for possible duplicate retry.
                    using var timeout = new CancellationTokenSource(options.Value.PublishTimeout, clock);
                    using var publishDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    await publisher.PublishAsync(message, row.Payload, publishDeadline.Token);

                    dispatch?.SetTag("outbox.status", "broker_confirmed");
                    row.PublishedAt = clock.GetUtcNow();
                    row.NextAttemptAt = null;
                    published++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Persist cooldown in the same PostgreSQL transaction as the batch.
                    // This keeps a failed early batch from starving later eligible messages.
                    row.PublishAttempts = row.PublishAttempts < int.MaxValue
                        ? row.PublishAttempts + 1 : int.MaxValue;
                    var multiplier = 1 << Math.Min(row.PublishAttempts - 1, 6);
                    var retrySeconds = Math.Min(options.Value.MaxRetryDelay.TotalSeconds,
                        options.Value.RetryDelay.TotalSeconds * multiplier);
                    row.NextAttemptAt = clock.GetUtcNow().AddSeconds(retrySeconds);
                    failures.Add(new PublishFailure(row.Id, row.ValueId, row.NextAttemptAt.Value, exception));
                }
            }

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BatchResult(published,
                messages.Where(x => x.PublishedAt is not null)
                    .Select(x => new PublishedMessage(x.Id, x.ValueId)).ToArray(),
                failures, quarantined);
        });

        // Do not emit externally visible events from the execution-strategy delegate:
        // a failed commit or replayed delegate must not duplicate success/failure logs
        // or counters for retry schedules that never became durable.
        foreach (var failure in batch.Failures)
        {
            OutboxTelemetry.PublicationFailures.Add(1);
            PublishFailed(logger, failure.MessageId, failure.ValueId,
                failure.NextAttemptAt, failure.Cause);
        }

        foreach (var quarantined in batch.Quarantined)
        {
            Quarantined(logger, quarantined.MessageId, quarantined.ValueId,
                quarantined.Reason, null);
        }

        foreach (var message in batch.PublishedMessages)
        {
            OutboxTelemetry.PublishedMessages.Add(1);
            Published(logger, message.MessageId, message.ValueId, null);
        }

        // Sample the complete pending backlog, including deferred retries, rather than
            // inferring it from the bounded claimed batch. Observability is best-effort.
            try
            {
                long pending = await database.OutboxMessages.AsNoTracking().LongCountAsync(
                    x => x.PublishedAt == null && x.QuarantinedAt == null, cancellationToken);
                OutboxTelemetry.PendingMessages.Record(pending);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                PendingCountFailure(logger, exception);
            }

        return batch.Published;
    }

    private void Quarantine(OutboxMessage row, string reason, List<QuarantinedMessage> quarantined)
    {
        row.QuarantinedAt = clock.GetUtcNow();
        row.QuarantineReason = reason;
        quarantined.Add(new QuarantinedMessage(row.Id, row.ValueId, reason));
    }

    private sealed record BatchResult(int Published, PublishedMessage[] PublishedMessages,
        List<PublishFailure> Failures, List<QuarantinedMessage> Quarantined);

    private sealed record PublishedMessage(Guid MessageId, Guid ValueId);

    private sealed record PublishFailure(Guid MessageId, Guid ValueId, DateTimeOffset NextAttemptAt,
        Exception Cause);

    private sealed record QuarantinedMessage(Guid MessageId, Guid ValueId, string Reason);
}
