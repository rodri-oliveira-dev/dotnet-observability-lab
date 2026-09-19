using System.Diagnostics;
using Consolidation.Persistence;
using Contracts;
using Microsoft.EntityFrameworkCore;
namespace Consolidation.Worker;
public enum ConsolidationResult { Applied, Duplicate }
/// <summary>Transport-neutral Inbox processor: one PostgreSQL transaction is the correctness boundary.</summary>
public sealed class ConsolidationProcessor(ConsolidationDbContext db, TimeProvider clock)
{
    public async Task<ConsolidationResult> ProcessAsync(ValueReceivedV1 message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.EventId == Guid.Empty || message.ValueId == Guid.Empty)
            throw new ArgumentException("EventId and ValueId must not be empty.", nameof(message));

        using var activity = ConsolidationTelemetry.Traces.StartActivity(
            "consolidation.process_value", ActivityKind.Internal);
        activity?.SetTag("message.id", message.MessageId.ToString("D"));
        activity?.SetTag("value.id", message.ValueId.ToString("D"));
        long started = Stopwatch.GetTimestamp();
        string result = "failed";
        try
        {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        // ON CONFLICT waits for concurrent transactions to settle; only the winner increments.
        var inserted = await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO inbox_messages (message_id, processed_at)
            VALUES ({0}, {1})
            ON CONFLICT (message_id) DO NOTHING
            """, [message.MessageId, now], cancellationToken);
        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            result = "duplicate";
            activity?.SetTag("consolidation.result", result);
            ConsolidationTelemetry.DuplicateMessages.Add(1);
            return ConsolidationResult.Duplicate;
        }
        // Singleton UPSERT serializes increments from different messages without lost updates.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO consolidated_totals (id, count, sum, last_updated_at)
            VALUES (1, 1, {0}, {1})
            ON CONFLICT (id) DO UPDATE SET
                count = consolidated_totals.count + 1,
                sum = consolidated_totals.sum + EXCLUDED.sum,
                last_updated_at = GREATEST(consolidated_totals.last_updated_at, EXCLUDED.last_updated_at)
            """, [message.Value, now], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        result = "applied";
        activity?.SetTag("consolidation.result", result);
        ConsolidationTelemetry.SuccessfulConsolidations.Add(1);
        return ConsolidationResult.Applied;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Inbox/consolidation persistence failed");
            throw;
        }
        finally
        {
            ConsolidationTelemetry.ProcessingDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("result", result));
        }
    }
}
