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
