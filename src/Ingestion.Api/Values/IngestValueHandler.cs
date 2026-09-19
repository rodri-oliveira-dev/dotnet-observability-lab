using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Contracts;
using Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Ingestion.Api.Values;

public sealed class IngestValueHandler(
    IngestionDbContext database,
    IDistributedCache cache,
    TimeProvider timeProvider,
    ILogger<IngestValueHandler> logger)
{
    private static readonly Action<ILogger, Exception?> CacheReadFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1001, "IdempotencyCacheLookupFailed"),
            "Idempotency cache lookup failed; falling back to PostgreSQL.");

    private static readonly Action<ILogger, Exception?> CacheWriteFailure =
        LoggerMessage.Define(LogLevel.Warning, new EventId(1002, "IdempotencyCacheWriteFailed"),
            "Idempotency cache write failed; committed PostgreSQL state is authoritative.");

    private static readonly Action<ILogger, Guid, string, Exception?> DuplicateDetected =
        LoggerMessage.Define<Guid, string>(LogLevel.Information,
            new EventId(1003, "IdempotencyDuplicate"),
            "Idempotency request {Result} for value {ValueId}; no new value or Outbox event committed.");

    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
    };

    public async Task<IngestValueResult> HandleAsync(
        string idempotencyKey, decimal value, CancellationToken cancellationToken)
    {
        using var activity = IngestionTelemetry.Traces.StartActivity("ingestion.accept_value", ActivityKind.Internal);
        // G29 normalizes decimal scale (e.g. 10.50 and 10.5) without JSON-specific formatting.
        string fingerprint = Sha256(value.ToString("G29", CultureInfo.InvariantCulture));
        string cacheKey = "ingestion:idempotency:v1:" + Sha256(idempotencyKey);

        var cached = await ReadCacheAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return RecordExisting(cached.Id, cached.Fingerprint == fingerprint
                ? new(IngestValueState.Replayed, new ValueReceipt(cached.Id, cached.Value))
                : new(IngestValueState.Conflict, null));
        }

        var existing = await database.ReceivedValues.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            await WriteCacheAsync(cacheKey, existing, cancellationToken);
            return RecordExisting(existing.Id, FromExisting(existing, fingerprint));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        var received = new ReceivedValue
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            Value = value,
            ReceivedAt = now
        };

        Guid eventId = Guid.NewGuid();
        var integrationEvent = new ValueReceivedV1(eventId, received.Id, value, now);
        var outbox = new OutboxMessage
        {
            Id = eventId,
            ValueId = received.Id,
            EventType = nameof(ValueReceivedV1),
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredAt = now,
            // Persist the originating W3C span context with the business Outbox transaction.
            TraceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } span ? span.Id : null,
            TraceState = Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.TraceStateString : null
        };

        try
        {
            database.ReceivedValues.Add(received);
            database.OutboxMessages.Add(outbox);
            // A single SaveChanges transaction commits both inserts atomically.
            // Do not start a user transaction outside Aspire's Npgsql retrying execution strategy.
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_received_values_idempotency_key" })
        {
            // PostgreSQL resolves concurrent inserts. EF rolls back the failed SaveChanges
            // transaction; the losing entries must not participate in the subsequent read.
            database.ChangeTracker.Clear();
            var winner = await database.ReceivedValues.AsNoTracking()
                .SingleAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            await WriteCacheAsync(cacheKey, winner, cancellationToken);
            return RecordExisting(winner.Id, FromExisting(winner, fingerprint));
        }

        // Report only after the durable value and Outbox commit. Cache is best-effort.
        activity?.SetTag("value.id", received.Id.ToString("D"));
        activity?.SetTag("message.id", eventId.ToString("D"));
        activity?.SetTag("idempotency.status", "created");
        IngestionTelemetry.AcceptedValues.Add(1);

        // A cache failure after commit must never change the successful HTTP result.
        await WriteCacheAsync(cacheKey, received, cancellationToken);
        return new(IngestValueState.Created, new ValueReceipt(received.Id, received.Value));
    }

    private IngestValueResult RecordExisting(Guid valueId, IngestValueResult result)
    {
        string status = result.State == IngestValueState.Replayed ? "replayed" : "conflict";
        Activity.Current?.SetTag("value.id", valueId.ToString("D"));
        Activity.Current?.SetTag("idempotency.status", status);
        IngestionTelemetry.DuplicateRequests.Add(1, new KeyValuePair<string, object?>("result", status));
        DuplicateDetected(logger, valueId, status, null);
        return result;
    }

    private static IngestValueResult FromExisting(ReceivedValue existing, string fingerprint) =>
        existing.RequestFingerprint == fingerprint
            ? new(IngestValueState.Replayed, new ValueReceipt(existing.Id, existing.Value))
            : new(IngestValueState.Conflict, null);

    private async Task<CacheEntry?> ReadCacheAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            string? json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<CacheEntry>(json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CacheReadFailure(logger, exception);
            return null;
        }
    }

    private async Task WriteCacheAsync(string key, ReceivedValue received, CancellationToken cancellationToken)
    {
        try
        {
            var entry = new CacheEntry(received.RequestFingerprint, received.Id, received.Value);
            await cache.SetStringAsync(key, JsonSerializer.Serialize(entry), CacheOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CacheWriteFailure(logger, exception);
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public sealed record CacheEntry(string Fingerprint, Guid Id, decimal Value);
}
