using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ingestion.Api.Values;

/// <summary>Low-cardinality, application-owned signals for the write boundary.</summary>
internal static class IngestionTelemetry
{
    internal const string Name = "Ingestion.Api";
    internal static readonly ActivitySource Traces = new(Name);
    internal static readonly Meter Metrics = new(Name);

    // Count only durable first writes; retries and conflicts never increment this counter.
    internal static readonly Counter<long> AcceptedValues =
        Metrics.CreateCounter<long>("lab.ingestion.values.accepted", unit: "{value}",
            description: "New business values committed together with an Outbox record.");

    // The result tag has precisely two known values; never tag a key, trace or business ID.
    internal static readonly Counter<long> DuplicateRequests =
        Metrics.CreateCounter<long>("lab.ingestion.requests.duplicate", unit: "{request}",
            description: "Idempotency replays and conflicting reused keys.");
}
