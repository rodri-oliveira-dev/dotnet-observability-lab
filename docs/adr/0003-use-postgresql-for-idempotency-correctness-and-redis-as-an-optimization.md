# Use PostgreSQL for idempotency correctness and Redis as an optimization

## Status
Accepted

## Decision
HTTP ingestion relies on a PostgreSQL unique idempotency key and atomic value/Outbox persistence.
The consolidation consumer relies on a PostgreSQL Inbox primary key on `MessageId` (the
`ValueReceivedV1.EventId`). Redis is optional; it is not consulted by the current
consumer, so eviction, outage and cache misses have no bearing on correctness.

A new delivery begins a transaction in `consolidation_db`, inserts its Inbox identity
with `ON CONFLICT DO NOTHING`, and updates the singleton consolidated read model only
if the insert succeeds. Both writes commit together. PostgreSQL arbitrates concurrent
inserts for the same identifier and serializes the singleton-row UPSERT for distinct
identifiers. A duplicate returns normally without a second aggregate update.

The consumer acknowledges only after the processor commits or identifies an already
committed Inbox record. A crash before commit rolls both writes back; a crash after
commit but before acknowledgement produces a safe redelivery.

Malformed messages (invalid JSON, missing identity, or mismatch with the AMQP
`MessageId`) are rejected without requeue to prevent a poison-message loop. The
currently declared queue has no DLX; an explicit dead-letter/quarantine facility is
a follow-up operational enhancement. Transient database errors are requeued, so a
prolonged outage can produce repeated deliveries; a bounded retry/backoff queue
is also follow-up work. Do not confuse transient requeue with an exactly-once
delivery guarantee.

## Consequences
- `Count`, `Sum` and `LastUpdatedAt` are persisted; `Average` is derived as
  `Sum / Count` and is zero for an empty model.
- The worker owns the consolidation migration and does not read `ingestion_db`.
- Redis never participates in the PostgreSQL transaction or message acknowledgement.
- Acknowledgements are at-least-once; business state is effectively once per `MessageId`.
