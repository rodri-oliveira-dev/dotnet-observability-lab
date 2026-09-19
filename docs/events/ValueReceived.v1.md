# ValueReceived.v1 — integration event contract

## Purpose and boundaries

Represents a value durably accepted by `Ingestion.Api`. The API writes the v1
JSON payload and its Outbox row together in `ingestion_db`; a later
`Ingestion.Outbox.Worker` publishes the event. The future
`Consolidation.Worker` consumes it and updates only `consolidation_db`.
The two HTTP APIs neither call one another nor interact directly with RabbitMQ.

The logical contract is `Contracts.ValueReceivedV1`, serialized using
`System.Text.Json` with the current property names and standard JSON number
and ISO-8601 timestamp representation. Existing v1 Outbox rows remain readable.

## Logical payload (v1)

| JSON property | Type | Meaning |
| --- | --- | --- |
| `EventId` | GUID | Stable, nonempty Outbox event identity; remains in v1 JSON for compatibility. |
| `ValueId` | GUID | Logical operation/value ID; business deduplication identity. |
| `Value` | JSON decimal number | Received numeric value, with no currency implied. |
| `OccurredAt` | ISO-8601 timestamp with offset | Time the value and Outbox entry were committed. |
| `CorrelationId` | Optional string | Caller/operation correlation only when already available; omitted when absent. |

`MessageId` is a C# alias of `EventId`, excluded from JSON to preserve the
persisted v1 representation. `EventId` is stable across publication attempts;
do not generate a new ID for retries.

Example:

```json
{
  "EventId": "0efc2134-a079-405b-a0b6-a6b3a677b33f",
  "ValueId": "c31a1f73-4c40-49b0-a556-7eaa3d589db7",
  "Value": 10.5,
  "OccurredAt": "2026-09-19T09:00:00+00:00"
}
```

## Transport metadata and routing (not logical JSON)

The transport adapter in `Messaging.RabbitMqTopology` centralizes the
following values. They must not be added to the logical JSON payload:

| Item | Value / mapping |
| --- | --- |
| `event_type` / AMQP `Type` | `ValueReceived.v1` |
| AMQP `MessageId` | `EventId` from the persisted Outbox event (GUID string) |
| AMQP `CorrelationId` | Optional logical correlation identifier when present |
| W3C `traceparent` and `tracestate` headers | Optional tracing context supplied by the future publisher; not business fields |
| Exchange | `lab.events.v1` (durable direct) |
| Queue | `consolidation.value-received.v1` (durable, non-exclusive, non-auto-delete) |
| Routing key | `value.received.v1` |

The current Outbox `event_type` column is `ValueReceivedV1` for the existing
v1 persistence flow. The future publisher must map that persisted name to the
versioned AMQP `Type`; do not silently rewrite existing Outbox data.

## Delivery and compatibility

The intended path is **at-least-once**. RabbitMQ may redeliver and the
publisher may retry. The future consumer must use a unique durable Inbox key
(`MessageId` / `EventId`) in its own PostgreSQL transaction with the
consolidated-state update; duplicate delivery is expected, and Redis is only
an optimization. Acknowledgements must follow durable commit.

A future breaking payload change requires a new explicit event version and
a consumer migration plan. Additive optional fields must remain tolerable to
older consumers. No event publishing or consuming is implemented by issue #5.
