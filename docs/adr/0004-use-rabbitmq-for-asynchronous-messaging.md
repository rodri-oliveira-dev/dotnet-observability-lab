# Use RabbitMQ for asynchronous ingestion-to-consolidation messaging

## Status

Accepted

## Context

Ingestion and consolidation are independently deployable runtime boundaries. The
consolidation HTTP API must remain available when the ingestion HTTP API is down.
HTTP calls between the two APIs would introduce temporal coupling and make
outages propagate. The ingestion side already commits a received value and a
pending Outbox record atomically in its own PostgreSQL database.

The lab requires one clearly observable event path with durable buffering and
explicit redelivery semantics. A shared database or distributed transaction
between boundaries would violate ownership and complicate local operation.

## Decision

Use one Aspire-managed RabbitMQ broker with a persistent local data volume and
management UI. Only the two worker processes receive broker connection
information. Neither HTTP API depends on RabbitMQ or calls the other API.

The topology is a durable direct exchange (`lab.events.v1`) and a durable,
non-exclusive, non-auto-delete consolidation queue
(`consolidation.value-received.v1`) with routing key `value.received.v1`.
The consolidation worker owns idempotent declaration of the exchange, queue and
binding on startup; the naming and declaration live in a shared transport-only
messaging adapter. Aspire registers a health check for the broker resource.

The existing `ValueReceivedV1` logical event is versioned as
`ValueReceived.v1`. Message identity and correlation are kept distinct from
routing details and provider-specific headers; the existing v1 JSON `EventId`
continues to identify the durable Outbox event for backward compatibility.
The future publisher will map this value to the AMQP message ID.

Delivery is **at-least-once**, not exactly-once. A publisher can retry after
uncertain broker confirmation and RabbitMQ can redeliver a message before an
acknowledgement. The future consolidation consumer must atomically persist the
Inbox message identity and its read-model update in `consolidation_db`; neither
RabbitMQ acknowledgement nor Redis deduplication alone establishes correctness.
No ordering guarantee is assumed beyond what a later consumer explicitly defines.

This issue provisions the resource, connection wiring, topology and contract;
publication, consuming, manual acknowledgements, retries and Inbox processing
are implemented in separate roadmap issues.

## Alternatives considered

- Direct HTTP: would tie the read-side update to availability and latency of the
  write API; rejected for the intended asynchronous boundary.
- Kafka: provides a durable event log and richer streaming features, but adds
  operating complexity not required for one queue and one event in this lab.
- A shared PostgreSQL table: would bypass the message boundary and break
  independent data ownership.

## Consequences

- Ingestion can commit and later publish independently of consolidation
  availability; consolidation HTTP reads never depend on ingestion HTTP.
- The durable broker queue buffers messages while the consumer is unavailable,
  subject to the broker's storage and operational limits.
- Broker availability affects the workers, not the two HTTP APIs.
- Duplicate messages are part of the contract. The later consumer must use a
  durable Inbox and explicitly control acknowledgement timing.
- Durable exchange/queue declarations alone do not make individual messages
  persistent: the future publisher must use persistent delivery and publisher
  confirmations, and handle unroutable messages and retries.
- Local RabbitMQ credentials must remain stable with its persistent data volume.
