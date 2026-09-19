# Use PostgreSQL for idempotency correctness and Redis as an optimization

## Status

Accepted

## Context

The lab will need idempotent execution at two external boundaries:

- repeated HTTP write requests on the ingestion side;
- duplicate or redelivered asynchronous messages on the consolidation side.

A fast distributed cache can reduce repeated database work, but a cache can be evicted, restarted, unavailable, or temporarily inconsistent with durable state.

Using Redis as the sole record of whether an operation or message has already been processed would make correctness depend on cache availability and retention.

PostgreSQL already provides durable transactions and unique constraints and is required for the business state on both boundaries.

## Decision

Use **PostgreSQL as the authoritative idempotency guarantee** and **Redis only as an optional fast path/optimization**.

The planned ownership is:

- `Ingestion.Api` may use Redis to short-circuit known repeated HTTP idempotency keys, while `ingestion_db` remains authoritative;
- `Consolidation.Worker` may use Redis to short-circuit known duplicate message identifiers, while the persistent Inbox in `consolidation_db` remains authoritative.

Redis loss, eviction, or temporary unavailability must never allow a duplicate operation to violate durable correctness.

No correctness-sensitive transaction spans PostgreSQL and Redis.

Redis is introduced as an Aspire resource in this issue so the intended topology is explicit, but the actual HTTP idempotency and consumer deduplication behavior is implemented by later issues.

The applications that do not participate in those fast paths do not receive a Redis reference.

## Consequences

Positive consequences:

- correctness survives Redis restart, eviction, or cache loss;
- PostgreSQL uniqueness can resolve races that occur after simultaneous cache misses;
- Redis can improve duplicate lookup latency without becoming a source of truth;
- the cache can be rebuilt from durable state or allowed to warm naturally.

Trade-offs and constraints:

- duplicate checks may still require PostgreSQL access;
- later implementations must define cache invalidation/retention behavior without assuming cache permanence;
- telemetry must distinguish durable idempotency decisions from cache hits/misses;
- Redis health must not be confused with the correctness of persisted business state.

## Implemented HTTP ingestion semantics (issue #4)

The ingestion API stores one received value with a unique Idempotency-Key, a SHA-256
fingerprint of the decimal value's invariant G29 representation, and one pending Outbox
event in the same PostgreSQL transaction.

- A repeated key with an equivalent numeric value returns the original value ID and HTTP 200;
  a first successful request returns HTTP 201.
- Reusing the key with a different numeric value returns HTTP 409 ProblemDetails without
  writing another value or event. Input errors return HTTP 400 ProblemDetails.
- The unique constraint in PostgreSQL settles concurrent inserts. A losing transaction
  reads the committed winning record after its own transaction is rolled back.
- The optional Redis entry stores only a committed result, with a bounded 24-hour TTL.
  Its cache key hashes the caller key rather than exposing it in a Redis key name.
  Cache misses, expiration and failures fall back to the authoritative database.
- The API never publishes to RabbitMQ. It stores a versioned integration event as a
  pending Outbox message; the independent worker claims pending rows with
  PostgreSQL row locks and commits PublishedAt only after broker confirmation.
  A crash after broker confirmation but before database commit may duplicate
  publication, so the future consumer must use a durable Inbox.

The ingestion API alone applies its schema migrations at startup; its worker shares the
persistence model but does not execute migrations. The consolidation boundary remains
independent and does not read the ingestion database.
