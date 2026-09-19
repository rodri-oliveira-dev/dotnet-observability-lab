using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Consolidation.Worker;

/// <summary>Application-owned processing signals, without per-message metric dimensions.</summary>
internal static class ConsolidationTelemetry
{
    internal const string Name = "Consolidation.Worker";
    internal static readonly ActivitySource Traces = new(Name);
    internal static readonly Meter Metrics = new(Name);

    internal static readonly Counter<long> ConsumedMessages =
        Metrics.CreateCounter<long>("lab.consolidation.messages.consumed", unit: "{message}",
            description: "RabbitMQ deliveries received, including redeliveries and malformed messages.");

    internal static readonly Counter<long> DuplicateMessages =
        Metrics.CreateCounter<long>("lab.consolidation.messages.duplicate", unit: "{message}",
            description: "Inbox duplicates identified by the persisted message ID.");

    internal static readonly Counter<long> SuccessfulConsolidations =
        Metrics.CreateCounter<long>("lab.consolidation.values.processed", unit: "{value}",
            description: "New events whose Inbox and consolidated read model committed atomically.");

    internal static readonly Histogram<double> ProcessingDuration =
        Metrics.CreateHistogram<double>("lab.consolidation.processing.duration", unit: "s",
            description: "PostgreSQL Inbox and consolidation processing latency, including duplicates and failures.");
}
