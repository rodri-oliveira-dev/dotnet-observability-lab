# Architectural case study: reliable asynchronous processing with observable evidence

This page is the canonical architectural case study for the lab. It connects the implemented problem, constraints, quality attributes, decisions, failure modes, observability signals, and repository evidence without creating a second architecture model.

**Architecture diagrams remain authoritative in [LikeC4](architecture/README.md).** This document links to those views and explains their architectural intent; it does not redraw them manually.

## Language strategy

Detailed technical documentation under `docs/` is canonical in English. The root [English README](../README.md) and [Português (Brasil) README](../README.pt-BR.md) provide equivalent project-level facts and both link to this case study. When a fact is repeated in both root READMEs, the two versions must remain factually equivalent; this case study is not duplicated solely for translation.

## 1. Problem

The lab demonstrates reliable asynchronous processing between independently executable .NET services while keeping the business domain intentionally small.

A client submits a decimal value to `Ingestion.Api`. Acceptance must be durable and idempotent. The accepted value must later reach the consolidation boundary without a synchronous API-to-API call. Publication can fail or be retried, RabbitMQ can redeliver, consumers can restart, and duplicate delivery must not double-count the aggregate. Operators must be able to inspect the resulting behavior through traces, structured logs, metrics, database state, and reproducible scenarios.

The implemented runtime path is represented by the authoritative [LikeC4 dynamic view](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read) and the versioned [`ValueReceived.v1` event contract](events/ValueReceived.v1.md).

## 2. Constraints

The case study is intentionally shaped by these explicit constraints:

- **The HTTP APIs do not call each other.** Ingestion and consolidation are independent runtime boundaries; the read API remains usable when the write API is unavailable. See [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md) and the [Container/Component views](architecture/README.md).
- **RabbitMQ delivery is at-least-once.** Duplicate or redelivered messages are expected. Correctness comes from durable idempotency, not an exactly-once transport promise. See [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md).
- **PostgreSQL is authoritative for durable state and deduplication.** Uniqueness and transactions protect HTTP idempotency, Outbox state, Inbox state, and aggregate updates. See [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md).
- **Redis is best-effort for HTTP idempotency.** It is a performance shortcut for committed receipts, never the durable correctness boundary; Redis loss or failure falls back to PostgreSQL. See [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md).
- **Aspire composes and observes the local environment; it is not a business router.** Business traffic flows through the APIs, databases, workers, and broker. See [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md).
- **Each service boundary owns its data.** One local PostgreSQL server hosts two logical databases, but ingestion code cannot query consolidation data and consolidation code cannot query ingestion data. See [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md).
- **The laboratory is educational, not a production reference topology.** It deliberately omits some production concerns such as bounded consumer retry/backoff and a dead-letter queue, and its single local PostgreSQL server is a local-development simplification.
- **The domain is deliberately narrow.** Receiving and aggregating a decimal value exists to expose reliability and observability mechanics rather than to model a rich business domain.

## 3. Quality attributes

| Quality attribute | Architectural response | Primary evidence |
| --- | --- | --- |
| Reliability | Transactional Outbox, publisher confirms, persistent RabbitMQ messages, durable Inbox, acknowledgement after commit | [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [Outbox processor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [consumer](../src/Consolidation.Worker/ConsolidationConsumer.cs) |
| Consistency | Value + Outbox commit atomically; Inbox + aggregate commit atomically; database uniqueness resolves races | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [ingestion tests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs), [consolidation tests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) |
| Observability | OpenTelemetry traces, metrics, structured logs, W3C propagation persisted across the Outbox boundary | [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [scenario runbook](scenarios.md) |
| Operability | Aspire composes resources, exposes health/telemetry locally, and gives a reproducible environment | [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs) |
| Recoverability | Pending Outbox rows survive publisher failures; duplicate redelivery is safe; durable reads survive ingestion outage | [Outbox tests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs), [scenario 4 and 5](scenarios.md) |
| Evolvability | Independently executable boundaries, versioned integration contract, separate logical databases, explicit ADRs and C4 model | [ValueReceived.v1](events/ValueReceived.v1.md), [ADR index](adr/README.md), [LikeC4 model](architecture/README.md) |
| Security | Separate non-superuser database roles, no cross-boundary database access, externalized secrets, CodeQL/dependency checks | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [CI documentation](ci.md), [CodeQL workflow](../.github/workflows/codeql.yml) |

## 4. Decisions and trade-offs

### 4.1 Asynchronous messaging instead of synchronous API-to-API calls

**Decision.** Ingestion and consolidation communicate through a versioned event published to RabbitMQ. Neither HTTP API depends on the broker or calls the other API.

**Why.** A synchronous call would make consolidation availability and latency part of the ingestion request path and would propagate outages across boundaries.

**Trade-off.** The system accepts eventual consistency and operational complexity around a broker and asynchronous failure handling.

**Evidence:** [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [LikeC4 dynamic view](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read), [Consolidation.Api component view](architecture/rendered.md#c4-level-3---consolidationapi).

### 4.2 Transactional Outbox for database-to-broker handoff

**Decision.** `Ingestion.Api` stores the accepted value and a pending Outbox message in the same PostgreSQL transaction. `Ingestion.Outbox.Worker` later claims pending rows and publishes them with mandatory routing, persistent delivery, and publisher confirmations.

**Why.** Writing business state and publishing directly to the broker cannot be made atomic with a normal local database transaction.

**Trade-off.** Publication becomes eventually consistent and requires polling, retry scheduling, quarantine handling, and duplicate-safe consumption.

**Evidence:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [ingestion handler](../src/Ingestion.Api/Values/IngestValueHandler.cs), [Outbox processor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [RabbitMQ publisher](../src/Ingestion.Outbox.Worker/RabbitMqOutboxMessagePublisher.cs), [Outbox tests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs).

### 4.3 Durable Inbox for consumer deduplication

**Decision.** `Consolidation.Worker` persists the event identity in `consolidation_db.inbox_messages` and updates the aggregate in the same PostgreSQL transaction.

**Why.** Broker redelivery is normal under at-least-once delivery, including the case where the consumer commits successfully but acknowledgement is lost.

**Trade-off.** Every unique delivery performs durable database work and the Inbox needs retention/operational policy in a real long-lived system.

**Evidence:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ConsolidationProcessor](../src/Consolidation.Worker/ConsolidationProcessor.cs), [Inbox entity](../src/Consolidation.Persistence/InboxMessage.cs), [concurrent duplicate tests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### 4.4 At-least-once plus idempotency instead of exactly-once claims

**Decision.** The transport contract explicitly permits repeated publication and redelivery. Business effects are made idempotent with durable identity.

**Why.** A publisher can lose certainty after broker acceptance and a consumer can commit before an acknowledgement reaches the broker. Calling this exactly-once would hide real failure semantics.

**Trade-off.** Consumers must be designed for duplicate delivery and observability must distinguish applied from duplicate work.

**Evidence:** [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [event contract](events/ValueReceived.v1.md), [duplicate AMQP scenario](scenarios.md), [consolidation telemetry tests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### 4.5 PostgreSQL correctness with Redis only as an optimization

**Decision.** PostgreSQL remains authoritative for HTTP idempotency and consumer deduplication. Redis caches only committed ingestion receipts and can fail without violating correctness.

**Why.** Cache eviction, restart, or temporary unavailability cannot be allowed to re-enable a duplicate durable operation.

**Trade-off.** Duplicate HTTP requests may still touch PostgreSQL, so the cache optimizes latency rather than replacing durable checks.

**Evidence:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs), [Redis-failure integration tests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs).

### 4.6 Separate logical databases instead of a shared schema

**Decision.** The local lab uses one PostgreSQL server resource but two separately owned databases: `ingestion_db` and `consolidation_db`, each with its own non-superuser application role.

**Why.** The arrangement keeps local resource cost low while making ownership and privilege boundaries explicit.

**Trade-off.** The local server remains a shared failure domain and does not prescribe a production topology.

**Evidence:** [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [database role provisioning](../src/DotNetObservabilityLab.AppHost/postgres-init/001-boundary-roles.sh).

### 4.7 OpenTelemetry as the standard, Aspire Dashboard as one local viewer

**Decision.** Traces, metrics, logs, and context propagation use OpenTelemetry semantics across all four processes. Aspire provides local orchestration and a convenient telemetry surface.

**Why.** Instrumentation remains portable instead of depending on a specific local dashboard.

**Trade-off.** The repository must maintain explicit instrumentation and propagation at asynchronous boundaries rather than relying on a product-specific correlation mechanism.

**Evidence:** [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [Ingestion telemetry](../src/Ingestion.Api/Values/IngestionTelemetry.cs), [Outbox telemetry](../src/Ingestion.Outbox.Worker/OutboxTelemetry.cs), [Consolidation telemetry](../src/Consolidation.Worker/ConsolidationTelemetry.cs).

### 4.8 Aspire for local composition, not production architecture or business routing

**Decision.** Aspire is the local composition root for processes and infrastructure and supplies the local Dashboard.

**Why.** It centralizes startup, resource wiring, health, and developer telemetry without leaking orchestration APIs into domain behavior.

**Trade-off.** Contributors need compatible Aspire tooling, and a production deployment platform still needs an independent decision.

**Evidence:** [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [LikeC4 System Context](architecture/rendered.md#c4-level-1---system-context).

## 5. Nominal flow

The authoritative representation is the [LikeC4 dynamic view](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read). In textual form:

`POST /values` → `ingestion_db` (value + Outbox) → `Ingestion.Outbox.Worker` → RabbitMQ → `Consolidation.Worker` → `consolidation_db` (Inbox + aggregate) → later independent `GET /consolidated`.

Key boundaries in that flow:

1. `Ingestion.Api` validates the request and idempotency key.
2. PostgreSQL atomically commits the received value and Outbox row.
3. The Outbox worker claims eligible rows and publishes `ValueReceived.v1`.
4. RabbitMQ may deliver the message one or more times.
5. The consolidation worker validates the wire identity and atomically commits Inbox + aggregate.
6. The message is acknowledged only after durable processing.
7. A later read request queries only `consolidation_db`; it is not part of the write trace.

For executable commands and expected telemetry, use [scenario 1](scenarios.md).

## 6. Failure modes

| Failure mode | Expected behavior | Verifiable evidence |
| --- | --- | --- |
| Duplicate HTTP request | Equivalent reuse returns the original receipt; conflicting reuse returns 409; exactly one durable value/Outbox effect remains | [Scenario 2](scenarios.md), [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs), [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md) |
| Duplicate/redelivered AMQP message | The worker may receive it again, but Inbox uniqueness prevents a second aggregate update; duplicate telemetry is emitted | [Scenario 3](scenarios.md), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) |
| Outbox worker interrupted | Already committed values/Outbox rows remain durable. On restart, eligible pending rows can be reclaimed and published | [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs), [Scenario 4](scenarios.md) |
| Failure after broker acceptance but before `PublishedAt` commit | The Outbox row may be published again; it keeps the same event identity and the consumer Inbox makes the repeated business effect safe | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [Outbox tests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) |
| RabbitMQ temporarily unavailable | Ingestion can still durably accept new values because the API does not publish directly; Outbox publication remains pending/retried and resumes when the broker recovers | [Scenario 5](scenarios.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md) |
| Redis unavailable/evicted | HTTP idempotency falls back to authoritative PostgreSQL; correctness is preserved although the fast path is lost | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) |
| Consolidation transaction fails after Inbox insertion attempt | The transaction rolls back both Inbox and aggregate; a transient delivery may be retried without a false processed marker | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs), [Scenario 6](scenarios.md) |
| Ingestion API unavailable | Existing consolidated reads remain available from `consolidation_db`; previously committed Outbox work can continue independently | [Scenario 4](scenarios.md), [ConsolidatedEndpointTests](../tests/Consolidation.Api.Tests/ConsolidatedEndpointTests.cs), [LikeC4 Consolidation.Api view](architecture/rendered.md#c4-level-3---consolidationapi) |

The [runtime verification report](runtime-verification.md) intentionally distinguishes **procedures and CI evidence** from a manually observed live Aspire run. Do not treat an unexecuted scenario description as proof that a runtime demonstration occurred.

## 7. Observability

The observability design follows [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md).

### Traces

- ASP.NET Core request instrumentation starts the ingestion request trace.
- The ingestion transaction stores the W3C trace context with the Outbox row.
- The Outbox worker creates publish processing spans and injects trace context into AMQP headers.
- The consolidation consumer extracts W3C context and continues the distributed write trace.
- A later `GET /consolidated` is an independent trace because it is a separate read request.

Relevant code and tests: [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [ConsolidationConsumer](../src/Consolidation.Worker/ConsolidationConsumer.cs), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### Structured logs

The handlers/workers emit named, structured events for outcomes such as accepted/replayed/conflicting idempotency requests, publication failures, applied messages, and duplicates. The runbook tells operators which log events to inspect for each scenario instead of relying on free-text console output.

### Metrics

The lab exposes low-cardinality counters/histograms for accepted values, duplicate requests, Outbox publication, consumed/duplicate messages, processed values, and processing duration. Tests assert bounded dimensions for key consolidation signals.

Relevant code and tests: [IngestionTelemetry](../src/Ingestion.Api/Values/IngestionTelemetry.cs), [OutboxTelemetry](../src/Ingestion.Outbox.Worker/OutboxTelemetry.cs), [ConsolidationTelemetry](../src/Consolidation.Worker/ConsolidationTelemetry.cs), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### Local inspection

Aspire starts the local resources and presents OpenTelemetry data in its Dashboard. It is an observation/composition surface, not a hop in the business path. The [scenario runbook](scenarios.md) maps expected traces, logs, metrics, and database/broker state to each reproducible failure mode.

## 8. Traceability matrix

The matrix below links architectural claims to implementation, tests, documentation/diagrams, and CI. CI proves deterministic repository gates; live outage demonstrations remain separately recorded in [runtime-verification.md](runtime-verification.md).

| Decision / claim | Implementation | Test evidence | Documentation / diagram | CI evidence |
| --- | --- | --- | --- | --- |
| Value + Outbox commit is the durable ingestion boundary | [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs), [ingestion persistence](../src/Ingestion.Persistence/IngestionDbContext.cs) | [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [dynamic view](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read) | [integration workflow](../.github/workflows/ingestion-integration.yml) |
| Outbox publisher confirms before marking success | [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [RabbitMqOutboxMessagePublisher](../src/Ingestion.Outbox.Worker/RabbitMqOutboxMessagePublisher.cs) | [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) | [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [Outbox component view](architecture/rendered.md#c4-level-3---ingestionoutboxworker) | [integration workflow](../.github/workflows/ingestion-integration.yml) |
| Duplicate AMQP delivery has one business effect | [ConsolidationProcessor](../src/Consolidation.Worker/ConsolidationProcessor.cs), [InboxMessage](../src/Consolidation.Persistence/InboxMessage.cs) | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [Scenario 3](scenarios.md) | [integration workflow](../.github/workflows/ingestion-integration.yml) |
| Redis is optional for correctness | [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs) | [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [Container view](architecture/rendered.md#c4-level-2---containers) | [integration workflow](../.github/workflows/ingestion-integration.yml) |
| Read and write boundaries are independent | [ConsolidatedQuery](../src/Consolidation.Api/Consolidated/ConsolidatedQuery.cs), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs) | [ConsolidatedEndpointTests](../tests/Consolidation.Api.Tests/ConsolidatedEndpointTests.cs) | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [Consolidation.Api view](architecture/rendered.md#c4-level-3---consolidationapi) | [architecture boundary + test gates](../.github/workflows/ingestion-integration.yml) |
| Observability uses portable OpenTelemetry semantics | [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [telemetry types](../src/Consolidation.Worker/ConsolidationTelemetry.cs) | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs), [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) | [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [scenario runbook](scenarios.md) | [integration workflow](../.github/workflows/ingestion-integration.yml), [CodeQL](../.github/workflows/codeql.yml) |
| Logical database ownership is enforced independently | [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [role provisioning](../src/DotNetObservabilityLab.AppHost/postgres-init/001-boundary-roles.sh) | PostgreSQL-backed suites under [tests/](../tests) exercise each boundary using its own persistence model | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [Container view](architecture/rendered.md#c4-level-2---containers) | [integration workflow](../.github/workflows/ingestion-integration.yml), [CodeQL](../.github/workflows/codeql.yml) |

The PR workflow also checks ADR Guard/index consistency, LikeC4 validation/format/build, architecture dependency rules, the five .NET test suites, and the configured global line-coverage gate. See [CI and automation](ci.md).

## 9. Deliberately not implemented

These omissions and simplifications are intentional and should not be copied into production without a separate decision:

- **No exactly-once transport claim.** The design embraces at-least-once delivery and idempotent effects.
- **No bounded consumer retry/backoff or dead-letter queue in v1.** Transient consumer failures can be requeued; production systems need an explicit poison-message and retry policy.
- **No production deployment architecture.** Aspire is the local composition/developer-experience layer only.
- **No separate PostgreSQL server per boundary in the local lab.** Separate logical databases and roles demonstrate ownership while keeping the environment small; production isolation should be decided independently.
- **No Redis authority.** Redis is deliberately unable to determine durable correctness.
- **No distributed transaction across PostgreSQL, RabbitMQ, and Redis.** Outbox/Inbox patterns replace cross-resource atomicity.
- **No event-ordering guarantee.** Consolidation correctness does not depend on strict message ordering.
- **No rich domain model or generalized messaging framework.** The lab keeps one event path so reliability mechanics remain inspectable.
- **No hand-maintained duplicate architecture diagrams.** LikeC4 under `docs/architecture` is the architecture source of truth.
- **No claim that scenario prose is runtime evidence.** Live observations must be recorded in [runtime-verification.md](runtime-verification.md).

## Navigation

- [Live interactive LikeC4 architecture](https://rodri-oliveira-dev.github.io/dotnet-observability-lab/)
- [GitHub-rendered architecture views](architecture/rendered.md)
- [Architecture decisions](adr/README.md)
- [Resilience and observability scenarios](scenarios.md)
- [Runtime verification report](runtime-verification.md)
- [CI and quality gates](ci.md)
