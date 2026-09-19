# dotnet-observability-lab

A small reference implementation for distributed observability with .NET Aspire and OpenTelemetry.

The repository intentionally keeps the business domain simple so that asynchronous reliability, idempotency, tracing, metrics, logs, ADRs, and C4 documentation remain easy to understand.

## Current scope

The current baseline provides:

- `Ingestion.Api`
- `Ingestion.Outbox.Worker`
- `Consolidation.Api`
- `Consolidation.Worker`
- one Aspire-managed PostgreSQL server with `ingestion_db` and `consolidation_db`
- distinct non-superuser PostgreSQL roles for the ingestion and consolidation boundaries
- Redis as an auxiliary optimization resource
- RabbitMQ with a persistent data volume, management UI and a durable exchange/queue/routing-key topology declared by the consolidation worker
- `DotNetObservabilityLab.AppHost`
- `DotNetObservabilityLab.ServiceDefaults`
- `Contracts`
- ADR governance with ADR Guard
- architecture-as-code with LikeC4

HTTP ingestion idempotency, transactional Outbox storage, independent RabbitMQ Outbox publication and the versioned ValueReceived.v1 contract are implemented. Inbox processing, consolidation, and application-level telemetry are introduced by later roadmap issues.

## Architecture style

The project uses a **lightweight Vertical Slice Architecture with explicit infrastructure boundaries**.

It intentionally does not use a full Clean Architecture project-per-layer structure. New abstractions should represent real boundaries or variation points, not architectural ceremony.

Core boundary rules:

- the two APIs never call each other;
- `Consolidation.*` must not depend on `Ingestion.*`;
- `Ingestion.*` must not depend on `Consolidation.*`;
- `Ingestion.*` can access only `ingestion_db` using the `ingestion_app` role;
- `Consolidation.*` can access only `consolidation_db` using the `consolidation_app` role;
- the PostgreSQL administrator credential is reserved for local provisioning/health and is not injected into application processes;
- Redis is an optimization, never the durable source of truth;
- ServiceDefaults contains only cross-cutting Aspire defaults;
- integration contracts belong in `Contracts`, not in either service's domain model.

Architecture documentation lives under [docs](docs/README.md). The LikeC4 model is the source of truth for diagrams, and ADR Guard validates architectural decisions.

## Prerequisites

- .NET 10 SDK (the repository pins `10.0.400` and rolls forward to the latest feature band)
- Aspire CLI compatible with Aspire 13.5
- Node.js 20+ for LikeC4 architecture tooling
- an OCI-compatible container runtime for PostgreSQL, Redis and RabbitMQ

## Restore and build

```bash
dotnet tool restore
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
```

## Validate architecture

```bash
dotnet tool run adr-guard check docs/adr
npm ci
npm run architecture:validate
npm run architecture:format:check
npm run architecture:build
```

Regenerate the deterministic ADR index after ADR changes:

```bash
dotnet tool run adr-guard index docs/adr
```

See [docs/architecture/README.md](docs/architecture/README.md) for database ownership, C4 levels, migration commands, model conventions, and maintenance rules.

## Local PostgreSQL secrets

The persistent PostgreSQL volume requires stable credentials across AppHost runs. Store the three local passwords in the AppHost user-secrets store before the first run:

```bash
aspire secret set Parameters:postgres-password <postgres-admin-password> --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:ingestion-db-password <ingestion-role-password> --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:consolidation-db-password <consolidation-role-password> --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-username <rabbitmq-username> --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-password <rabbitmq-password> --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

Do not commit these values. The AppHost has a `UserSecretsId`, so the secrets are stored outside the repository.

If a disposable PostgreSQL data volume was created before these stable credentials and boundary roles were introduced, stop the AppHost and recreate that local volume once. Use `docker volume ls` (or the equivalent command for your container runtime) to identify the AppHost PostgreSQL data volume, then remove only that disposable development volume. Existing non-disposable data should be migrated instead of deleted.

Changing any of the PostgreSQL passwords later also requires updating the corresponding
PostgreSQL role/password in the existing volume or recreating a disposable local volume.
The RabbitMQ username/password must also stay stable while its data volume persists;
use them to sign in to the RabbitMQ management UI linked in the Aspire Dashboard.

## Run

Start the AppHost:

```bash
aspire run --project ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

The Aspire Dashboard should show:

- `ingestion-api`
- `ingestion-outbox-worker`
- `consolidation-api`
- `consolidation-worker`
- `postgres`
- `ingestion-db`
- `consolidation-db`
- `redis`
- `rabbitmq` (with a management UI endpoint)

The applications receive only role-specific PostgreSQL connection strings for their owned database. Redis is referenced only by `Ingestion.Api` and `Consolidation.Worker`. RabbitMQ is referenced only by the two workers. Both workers idempotently declare the durable broker topology. The consolidation worker does not consume messages yet. The independent ingestion Outbox worker publishes pending messages with broker confirmations.

## Ingest a value

After configuring local Aspire secrets and starting the AppHost, use its published
Ingestion.Api base URL as INGESTION_API_URL:

~~~bash
curl -i -X POST "$INGESTION_API_URL/values" \
  -H 'Idempotency-Key: order-123' \
  -H 'Content-Type: application/json' \
  -d '{"value":10.5}'
~~~

A new key returns HTTP 201 with a JSON receipt containing id and value. Repeating
the same key with an equivalent numeric payload returns HTTP 200 with the same
receipt; a different value under that key returns HTTP 409 ProblemDetails.
Invalid or missing keys and values return HTTP 400 ProblemDetails.

The API applies its ingestion database migrations at startup. Each accepted request
persists a value and a pending ValueReceivedV1 Outbox message in one PostgreSQL
transaction. Redis caches only committed receipts for 24 hours and is optional for
correctness; the API does not publish directly to RabbitMQ.

Integration tests require a running Docker-compatible engine. Contract tests do not require Docker:

~~~bash
dotnet test ./tests/Ingestion.Api.Tests/Ingestion.Api.Tests.csproj --configuration Release
dotnet test ./tests/Messaging.Contracts.Tests/Messaging.Contracts.Tests.csproj --configuration Release
~~~

## Verify Outbox publication while the API is stopped

Start Aspire and POST a value. Stop **only** the `ingestion-api` process
in the Aspire Dashboard; keep PostgreSQL, `ingestion-outbox-worker` and
RabbitMQ running. The worker drains committed Outbox records without the API.
Inspect `consolidation.value-received.v1` in the RabbitMQ management UI:
messages remain queued until the Inbox consumer is implemented.

Connect to `ingestion_db` with the ingestion application credentials and
inspect the pending-to-confirmed transition:

```sql
SELECT "Id", event_type, occurred_at, published_at, quarantined_at, quarantine_reason,
       publish_attempts, next_attempt_at
FROM outbox_messages
ORDER BY occurred_at DESC
LIMIT 20;
```

`published_at` is set only after the broker confirms publication. Unsupported event types,
malformed JSON and invalid message identities are quarantined with a reason and skipped
by subsequent polls; inspect and repair them deliberately rather than repeatedly retrying
poison rows. A transport failure or publish timeout does **not** quarantine the row:
its persisted `next_attempt_at` postpones another attempt, using bounded exponential
backoff so failed messages do not repeatedly occupy the oldest pending batch.
To test retry, stop RabbitMQ, restart the API briefly to POST another value, then
stop the API. That Outbox row stays pending until RabbitMQ returns. A crash
between broker confirmation and database commit can lead to a duplicate
event with the **same** message ID; the consumer will require an Inbox.

The worker's `Outbox` settings are `BatchSize` (default 10, maximum 100),
`PollInterval` (default 2 seconds), `PublishTimeout` (default 10 seconds),
`RetryDelay` (default 5 seconds) and `MaxRetryDelay` (default 5 minutes).
The retry counter and next-attempt time survive worker restarts. Multiple worker instances use `FOR UPDATE SKIP LOCKED` rather
than a Redis lock. The worker does not require a running HTTP API.

Outbox integration tests require a Docker-compatible engine:

```bash
dotnet test ./tests/Ingestion.Outbox.Worker.Tests/Ingestion.Outbox.Worker.Tests.csproj --configuration Release
```

## Repository conventions

The engineering baseline is adapted from `rodri-oliveira-dev/poc-arquitetura`:

- Central Package Management;
- deterministic builds;
- NuGet audit and package pruning;
- analyzer-driven `.editorconfig`;
- Conventional Commits;
- small, justified changes;
- architecture documentation evolves with architecture changes.

See [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md).
