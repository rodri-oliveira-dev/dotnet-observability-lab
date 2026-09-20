# Architecture

LikeC4 is the source of truth for architecture diagrams in this repository.

The model is intentionally small and follows the same principle as the implementation: document only distinctions that help explain ownership, runtime boundaries, reliability, and observability.

## Architecture style

The implementation style is:

> **Lightweight Vertical Slice Architecture with explicit infrastructure boundaries.**

The repository deliberately avoids a full Clean Architecture project-per-layer structure. Architecture should remain proportional to the small application while making the important boundaries explicit.

The two persistence projects are boundary-internal implementation libraries shared by each boundary's API and Worker. They exist to keep one authoritative `DbContext`, mappings, and migration history per logical database; they are not a general project-per-layer architecture.

## C4 levels

The repository uses three C4 levels **and a LikeC4 dynamic write/read flow** (`valueWriteFlow`).

### Level 1 — System Context

Answers:

> What is this system, who interacts with it, and what sits outside its functional boundary?

Current view: `systemContext`.

It shows the API client, developer, the lab itself, and Aspire as development tooling.

### Level 2 — Container

Answers:

> Which independently executable applications and data/messaging resources form the system?

Current view: `containers`.

The four executable processes are:

- `Ingestion.Api`
- `Ingestion.Outbox.Worker`
- `Consolidation.Api`
- `Consolidation.Worker`

The current data topology is one Aspire-managed PostgreSQL server resource containing two separately owned logical databases:

| Boundary | Database | Application role | Allowed processes |
| --- | --- | --- | --- |
| Ingestion | `ingestion_db` | `ingestion_app` | `Ingestion.Api`, `Ingestion.Outbox.Worker` |
| Consolidation | `consolidation_db` | `consolidation_app` | `Consolidation.Api`, `Consolidation.Worker` |

The PostgreSQL administrator credential is used only by the local PostgreSQL resource for provisioning and health operations. It is not injected into application processes.

Redis is a current Aspire resource, with two distinct kinds of connection:

- **Active runtime use:** `Ingestion.Api` uses Redis for best-effort HTTP idempotency receipt lookups after durable PostgreSQL writes. A cache miss or failure falls back to `ingestion_db`.
- **Provisioned reference only:** `AppHost.cs` passes a Redis reference to `Consolidation.Worker`, but its `Program.cs` does not register a Redis client and the consumer does not query the cache. A possible consumer duplicate shortcut is post-v1 work, not a step in the implemented flow.

The LikeC4 runtime relationship graph therefore contains an ingestion-to-Redis edge **but no consolidation-worker-to-Redis edge**; provisioning alone is not an active business dependency. The current consumer receives `ValueReceived.v1` from RabbitMQ and atomically persists its `EventId` (`MessageId`) in the PostgreSQL Inbox with the aggregate update in `consolidation_db`. PostgreSQL uniqueness handles duplicates, including concurrent deliveries, independently of Redis availability. A transient processing failure may be requeued; bounded consumer retries and a dead-letter queue are not implemented in v1.

RabbitMQ is now an Aspire-managed broker with persistent data, management UI, and a
worker-owned durable direct exchange (`lab.events.v1`) bound to a durable queue
(`consolidation.value-received.v1`) by routing key `value.received.v1`.
Only `Ingestion.Outbox.Worker` and `Consolidation.Worker` receive the RabbitMQ
reference; the consolidation worker declares the topology when it starts.
Both APIs remain independent of the broker. The Outbox worker independently
publishes durable messages with broker confirmations; the consolidation worker
atomically commits the Inbox and read model before acknowledging deliveries. Either worker may idempotently
declare the durable topology even when the other is offline.

The transport contract is **at-least-once**: the consumer persists the
Inbox identity with the read-model update and tolerates redelivery. See
[ValueReceived.v1](../events/ValueReceived.v1.md) for contract fields and
transport metadata.

The `Contracts` and `*.Persistence` projects are not shown as runtime containers because they are code/build dependencies, not separately running processes.

The read API continues to answer `GET /consolidated` from `consolidation_db`
when ingestion, RabbitMQ or the consumer is stopped. The model shows no
`Consolidation.Api` → `Ingestion.Api` relationship. Database readiness for
the read API depends only on `consolidation_db`.

### Level 3 — Component

Answers:

> How is one application boundary organized internally around its meaningful responsibilities?

The ingestionComponents view now shows the implemented HTTP endpoint, idempotent ingestion use case, and transactional EF Core persistence boundary. The outboxPublisherComponents view models the dedicated poller, the confirmed RabbitMQ adapter and their infrastructure boundaries. The consolidationComponents view models the active RabbitMQ consumer, transactional Inbox processor and PostgreSQL store. The consolidationApiComponents view shows the independent HTTP read endpoint and the read-only database query, with no synchronous dependency on Ingestion.Api or RabbitMQ.

Do not create a component for every class. A component should represent a meaningful responsibility, boundary, port, adapter, hosted service, or processing stage.

### Dynamic write/read flow

The [LikeC4 dynamic view](views.c4) `valueWriteFlow` orders one successful POST, its durable Outbox handoff, the broker-confirmed event, the PostgreSQL Inbox plus aggregate transaction (without a Redis consumer lookup), and a later **separate** GET. It models one scenario, not additional static dependencies or a promise of a single atomic distributed transaction. The AppHost/Dashboard receives OTLP export from the processes, but is not a hop in the business flow. Follow [the hands-on scenarios](../scenarios.md) to compare this intended path with real sampled traces, structured logs, and meters. A GET is a new HTTP trace; delays, failed attempts and duplicate deliveries can add runtime spans that are not steps in the nominal dynamic view.

## Database ownership

The local environment deliberately uses one PostgreSQL container for operational simplicity, but that does not create a shared database model.

Rules:

- `ingestion_db` is owned by the non-superuser `ingestion_app` role;
- `consolidation_db` is owned by the non-superuser `consolidation_app` role;
- `PUBLIC CONNECT` is revoked from both application databases;
- each application role is granted `CONNECT` only to its own database;
- the application connection strings never contain the PostgreSQL administrator credential;
- no cross-database queries;
- no shared tables;
- each boundary owns its EF Core mappings and migrations;
- schema changes create new migrations rather than rewriting migration history.

Because PostgreSQL 15+ makes the current database owner the implicit `pg_database_owner`, the owner governs the database's `public` schema. This lets each boundary run its own EF migrations without granting a cross-boundary or superuser role.

The local single-server topology is not a production deployment decision.

## Local credential lifecycle

The PostgreSQL data volume persists across AppHost restarts, so the administrator and boundary-role passwords must remain stable for the lifetime of that volume.

The AppHost declares these secret parameters:

- `postgres-password`
- `ingestion-db-password`
- `consolidation-db-password`

Set them using `aspire secret set` (or another standard .NET configuration source) rather than source-controlled configuration.

The PostgreSQL init script creates the two non-superuser application roles only when a new PostgreSQL data volume is initialized. If a disposable local volume predates these roles, recreate that volume once. If the data is not disposable, migrate it and alter the roles/passwords instead of deleting the volume.

## EF Core migration bootstrap

Restore the local EF tool:

```bash
dotnet tool restore
```

Create a migration for the ingestion boundary:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Ingestion.Persistence \
  --context IngestionDbContext \
  --output-dir Migrations
```

Create a migration for the consolidation boundary:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Consolidation.Persistence \
  --context ConsolidationDbContext \
  --output-dir Migrations
```

The design-time factories use a local, passwordless placeholder connection string because migration generation does not require a live database. Set `INGESTION_DB_CONNECTION_STRING` or `CONSOLIDATION_DB_CONNECTION_STRING` when a design-time operation actually needs to connect to a database.

Runtime applications do not use those environment variables; the AppHost injects only the role-specific connection string for the boundary-owned database.

## Aspire and OpenTelemetry

The documentation distinguishes two concerns:

```text
.NET Aspire
→ local composition
→ resource wiring / developer experience
→ local dashboard

OpenTelemetry
→ instrumentation
→ context propagation
→ traces / metrics / logs
→ portable telemetry standard
```

Aspire must not be drawn as if business requests or integration events pass through the AppHost/Dashboard.

[ADR 0005](../adr/0005-use-opentelemetry-as-the-observability-standard.md)
establishes OpenTelemetry as the common trace, metrics and structured-log
standard. ServiceDefaults configures automatic ASP.NET Core, HttpClient and
runtime instrumentation plus OTLP export to the local Aspire Dashboard.

The write request's W3C context is committed alongside the Outbox event;
the publisher restores it into a producer span and injects RabbitMQ headers,
which the consolidation worker extracts into a consumer span around the Inbox
transaction. The later read request starts a **separate HTTP trace**.
Missing or invalid context does not block business processing.

Custom application spans for first-time ingestion and Inbox consolidation and
a bounded set of service-local System.Diagnostics.Metrics instruments add
operational decision visibility to those existing process boundaries. They do
not introduce a separately deployed observability component or alter
business-message relationships in LikeC4. The meter names are explicitly
registered in ServiceDefaults, and metric dimensions exclude all per-request
identifiers. See [the observability/scenario runbook](../scenarios.md) for the metric contracts and duplicate/retry demonstrations.

The C4 Container view shows four explicit **OTLP telemetry export** edges
from the processes to the Aspire Dashboard; those edges are not API calls or
broker/business-event delivery. Point the standard OTLP exporter to another
compatible collector when running outside the local development environment.

## Files

- `model.c4` — element definitions, ownership, descriptions, and relationships.
- `views.c4` — C4 views, the dynamic write/read view and the small shared visual convention.

Keep model facts in `model.c4`; avoid duplicating the same relationship merely to make a view look better.

## Visual convention

The model uses a deliberately small tag set:

| Tag | Meaning |
| --- | --- |
| `api` | HTTP API process |
| `worker` | Background process without an HTTP API surface |
| `database` | Persistent data store |
| `cache` | Cache or optimization store |
| `broker` | Message broker |
| `orchestration` | Aspire/local development tooling |
| `external` | Actor outside the system |
| `future` | Planned element that is not yet implemented |

Future elements use dotted/muted styling so planned architecture is not confused with the current runtime.

## Validate locally

Install the pinned Node dependency and validate the model:

```bash
npm ci
npm run architecture:validate
npm run architecture:format:check
```

Build the relocatable static site:

```bash
npm run architecture:build
```

The generated site is written to `dist/architecture` and is intentionally not committed.

Preview interactively during architecture work:

```bash
npm run architecture:dev
```

LikeC4 requires Node.js 20 or newer.

## ADR workflow

ADR Guard is installed as a repository-local .NET tool.

Restore and validate:

```bash
dotnet tool restore
dotnet tool run adr-guard check docs/adr
```

Regenerate the deterministic ADR index:

```bash
dotnet tool run adr-guard index docs/adr
```

The generated `docs/adr/README.md` must be committed when it changes.

## When architecture documentation must change

Update the LikeC4 model in the same pull request when a change alters one or more of:

- executable processes;
- process responsibility or ownership;
- database/cache ownership;
- messaging resources or delivery relationships;
- synchronous or asynchronous service relationships;
- important component boundaries;
- observability/runtime relationships represented by the model.

Create or update an ADR when a change introduces or materially changes an architectural decision with plausible alternatives and meaningful consequences.

Do not create ADRs for routine implementation details merely because they are new.

## Relationship between documentation types

- **ADRs** record architectural decisions and their reasoning.
- **LikeC4** shows the resulting current/planned architecture.
- **README** provides operational entry points and navigation.
- [Scenario runbook](../scenarios.md) explains how to execute and troubleshoot the system.

An ADR is historical decision context; LikeC4 is the architecture model that should remain synchronized with the implementation.
