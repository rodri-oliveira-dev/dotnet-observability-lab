# Use OpenTelemetry as the observability standard

## Status

Accepted

## Context

The lab's HTTP request, durable Outbox, independent publisher, RabbitMQ and
consolidation worker run in separate processes. A database commit and a later
broker delivery break the default in-process trace continuity. The project uses
.NET Aspire for local composition and its Dashboard as a convenient OTLP
receiver, but business instrumentation should remain portable beyond Aspire.

Coupling telemetry directly to Dashboard-specific APIs would hinder the move to
another observability backend. Automatic HTTP and runtime instrumentation alone
cannot reconstruct an asynchronous parent relationship across a stored Outbox
record or identify message-specific processing steps.

## Decision

Use **OpenTelemetry** for the common tracing, metrics and structured-log
pipeline across all four processes. Configure shared ASP.NET Core, HttpClient,
runtime and OTLP instrumentation in ServiceDefaults; use the process name as the
service resource identity and register the two worker ActivitySources. The
Aspire Dashboard is the **local visualization destination**, not the telemetry
standard or part of the functional message path. Other OTLP-compatible
collectors can be configured through the exporter endpoint without changing
business code.

For an accepted HTTP write, persist the current W3C `traceparent` and optional
`tracestate` on the Outbox row in the same transaction as the value and event.
The Outbox worker parses the persisted parent, starts a producer activity, and
injects that activity's W3C context into the RabbitMQ `traceparent` and
`tracestate` headers. The consolidation worker extracts this context and starts
a consumer activity around Inbox processing. Identifiers such as MessageId
remain business correlation attributes; they are not fabricated TraceIds.

No required business action depends on a valid tracing context. Legacy Outbox
records and third-party deliveries with absent or malformed context start an
independent trace. A later `GET /consolidated` is a separate HTTP request and
is never linked artificially to the original write trace.

## Consequences

- Distributed traces preserve causal relationships across independent processes
  and arbitrary Outbox delays; supported tracing, metrics and logs share OTLP.
- The source of truth remains PostgreSQL, while tracing context is only optional
  operational metadata, not a business key, deduplication input or authorization data.
- Automatic instrumentation covers HTTP/runtime and supported client operations;
  small custom producer/consumer spans cover the asynchronous boundaries that
  automatic instrumentation cannot reconstruct.
- An instrumentation or exporter outage must not prevent requests, publication or
  consolidation. Optional context fields accommodate pre-existing Outbox rows.
- Trace sampling can omit spans from the visualized chain, and the Aspire Dashboard
  remains a development visualization rather than a production telemetry backend.
- Trace context and logs can carry operational metadata; do not put request bodies,
  idempotency keys, secrets or sensitive values into span tags.
