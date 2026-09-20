# dotnet-observability-lab

A small, runnable **.NET 10 / Aspire / OpenTelemetry** reference for observing reliable asynchronous delivery, HTTP idempotency and independent reads. The business operation is deliberately just a decimal value: the interesting behavior is in durable boundaries, duplicate handling and telemetry—not a large domain or unnecessary framework abstractions.

## Architecture at a glance

```text
POST /values -> Ingestion.Api -> ingestion_db (value + transactional Outbox)
                                  -> Ingestion.Outbox.Worker -> RabbitMQ
                                  -> Consolidation.Worker -> consolidation_db (Inbox + total)
GET /consolidated -> Consolidation.Api ---------------------> consolidation_db
```

The two APIs **never call each other**. A single local PostgreSQL resource hosts two separate logical databases (`ingestion_db` and `consolidation_db`), each with its own non-superuser owner/credentials. Redis accelerates ingestion idempotency but never establishes correctness; only the two workers connect to RabbitMQ. Aspire starts local processes/resources and displays OpenTelemetry data; it does **not** route business traffic. See [C4 Levels 1–3 and the dynamic flow](docs/architecture/README.md), the [event contract](docs/events/ValueReceived.v1.md), and [architectural decisions](docs/adr/README.md).

## Prerequisites and start

Install the SDK pinned in [global.json](global.json) (.NET 10), an Aspire CLI compatible with the AppHost, and a running Docker/OCI-compatible engine. Node.js 20+ is needed for the architecture/CI tooling but **not** to run the AppHost. To initialize a fresh local environment, set five persistent secrets once (choose your own strong values; do not commit them):

```bash
aspire secret set Parameters:postgres-password YOUR_ADMIN_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:ingestion-db-password YOUR_INGESTION_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:consolidation-db-password YOUR_CONSOLIDATION_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-username YOUR_RABBITMQ_USERNAME --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-password YOUR_RABBITMQ_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

The same secrets must be retained across restarts of the persistent PostgreSQL and RabbitMQ volumes. Existing volumes initialized under different credentials may require updating roles/passwords or recreating **only disposable local data**. From the repository root, start everything with **one command**:

```bash
aspire run --project ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

Open the Dashboard URL printed by Aspire. Wait for `ingestion-api`, `ingestion-outbox-worker`, `consolidation-api`, `consolidation-worker`, `postgres`, `ingestion-db`, `consolidation-db`, `rabbitmq` and `redis` to be healthy. Copy each HTTP endpoint's URL from the resource list into your shell (these ports are assigned at runtime):

```bash
export INGESTION_API_URL='http://localhost:PORT_FROM_ASPIRE'
export CONSOLIDATION_API_URL='http://localhost:OTHER_PORT_FROM_ASPIRE'
curl -i -X POST "$INGESTION_API_URL/values" \
  -H 'Idempotency-Key: demo-001' -H 'Content-Type: application/json' \
  -d '{"value":10.5}'
curl -i "$CONSOLIDATION_API_URL/consolidated"
```

A new key returns **201** with `id` and `value`; the same key and numeric value return **200** with the original receipt, while the same key with a different value returns **409**. Once the workers finish, `GET /consolidated` returns HTTP 200 with `count`, `sum`, `average` and `lastUpdatedAt`. Before the first event, it returns zeros and a null timestamp. The read API answers from its own database even if ingestion is unavailable.

## Explore behavior

The [hands-on scenario runbook](docs/scenarios.md) provides commands, expected database state and **Aspire Traces / Structured Logs / Metrics** signals for six demonstrations: successful end-to-end processing, duplicate HTTP requests, duplicate AMQP delivery, Ingestion.Api downtime, temporary RabbitMQ outage/recovery, and slow/error consolidation. It includes reversible **development-only SQL fault injection** rather than toggles in production application code. Use the [dynamic LikeC4 view](docs/architecture/views.c4) to understand the nominal order; an actual distributed write trace follows the persisted W3C context across the Outbox, whereas a later GET begins a separate trace.

## Build, tests and quality gates

```bash
dotnet tool restore
npm ci
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
bash scripts/test-with-coverage.sh
```

The five .NET test suites exercise HTTP, Outbox, Inbox, independent read and contract behavior; PostgreSQL/Testcontainers integration tests need Docker. The PR workflow also verifies architecture boundary regressions, ADR Guard/index, LikeC4 validate/format/build and **at least 80% global line coverage**. Separate CodeQL and Dependency Review workflows check security/dependency changes. For the complete local gate sequence, use [docs/ci.md](docs/ci.md). The [documentation index](docs/README.md) holds the longer implementation and operational details; this README stays a quick-start rather than a tutorial for every framework.
