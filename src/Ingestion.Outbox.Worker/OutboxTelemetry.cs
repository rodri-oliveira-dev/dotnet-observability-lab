using System.Diagnostics.Metrics;

namespace Ingestion.Outbox.Worker;

/// <summary>Service-local, low-cardinality Outbox reliability instruments.</summary>
internal static class OutboxTelemetry
{
    internal const string Name = "Ingestion.Outbox.Worker";
    internal static readonly Meter Metrics = new(Name);

    internal static readonly Counter<long> PublishedMessages =
        Metrics.CreateCounter<long>("lab.outbox.messages.published", unit: "{message}",
            description: "Broker-confirmed messages durably marked published in PostgreSQL.");

    internal static readonly Counter<long> PublicationFailures =
        Metrics.CreateCounter<long>("lab.outbox.messages.publish_failures", unit: "{attempt}",
            description: "Failed Outbox publish attempts scheduled for retry.");

    internal static readonly Gauge<long> PendingMessages =
        Metrics.CreateGauge<long>("lab.outbox.messages.pending", unit: "{message}",
            description: "Latest sampled count of unquarantined, unpublished PostgreSQL Outbox rows; includes scheduled retries.");
}
