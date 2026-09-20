[**English (current)**](README.md) | [Português (Brasil)](README.pt-BR.md)

# dotnet-observability-lab

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) [![.NET Aspire](https://img.shields.io/badge/.NET-Aspire-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/aspire/) [![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-instrumented-425CC7)](https://opentelemetry.io/)  
[![Build, tests and coverage — main](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/ingestion-integration.yml/badge.svg?branch=main)](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/ingestion-integration.yml) [![CodeQL — main](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/codeql.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![Architecture: LikeC4](https://img.shields.io/badge/Architecture-LikeC4-606C38)](docs/architecture/README.md)

An educational, runnable **.NET 10 / Aspire / OpenTelemetry** reference lab for reliable asynchronous processing, HTTP idempotency, and distributed tracing across independent APIs and workers. The deliberately simple business operation handles a decimal value: the focus is durable boundaries, at-least-once delivery, duplicate handling, and telemetry—not a large domain or unnecessary framework abstractions.

## Architecture at a glance

**[View the rendered C4 Container overview directly on GitHub](docs/architecture/rendered.md#c4-level-2---containers)** · [Browse all seven rendered LikeC4 views](docs/architecture/rendered.md) · [Download interactive site and PNG previews](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/architecture-preview.yml)

The [LikeC4 architecture model and view index](docs/architecture/README.md) are the source of truth for the C4 System Context, Container, Component and dynamic flow views. The gallery above is generated from the same `.c4` model as GitHub-rendered Mermaid, not a second hand-maintained diagram. For an interactive preview from a fresh clone, run `npm ci && npm run architecture:dev` in the repository root and open the URL shown by LikeC4. The workflow linked above offers a downloadable static site and PNG exports; this README does not claim a publicly deployed Pages site.

**Nominal data path (textual overview, not a second C4 diagram):** `POST /values` → `ingestion_db` (value + Outbox) → `Ingestion.Outbox.Worker` → RabbitMQ → `Consolidation.Worker` → `consolidation_db` (Inbox + aggregate). A **later, independent** `GET /consolidated` reads `consolidation_db`.

The two APIs **never call each other**. A single local PostgreSQL resource hosts two separate logical databases (`ingestion_db` and `consolidation_db`), each with its own non-superuser owner/credentials. Redis is an optional, best-effort HTTP idempotency shortcut in `Ingestion.Api`; PostgreSQL remains authoritative for both HTTP idempotency and consumer deduplication. Although Aspire provisions a Redis reference for `Consolidation.Worker`, the worker makes no Redis lookup: it atomically records the Inbox message ID and updates the aggregate in `consolidation_db`. Only the two workers connect to RabbitMQ; delivery is at-least-once, and the PostgreSQL Inbox makes its **business effect idempotent**, not its transport exactly-once. Aspire starts local processes/resources and displays OpenTelemetry data; it does **not** route business traffic. See [C4 Levels 1–3 and the dynamic flow](docs/architecture/README.md), the [event contract](docs/events/ValueReceived.v1.md), and [architectural decisions](docs/adr/README.md).

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

The [live runtime verification report](docs/runtime-verification.md) distinguishes executed evidence from the scenario procedures and CI tests; its six-scenario status is **pending** until the Aspire Dashboard, message replay, outages and database checks have actually been inspected. The optional [HTTP smoke harness](scripts/runtime_http_smoke.py) verifies only the first two scenarios' HTTP/read-model observations against a running disposable environment.

## Troubleshooting first run

If Aspire cannot start PostgreSQL, RabbitMQ or Redis, make sure the Docker/OCI engine is running and its resources are healthy. If startup reports missing parameters, set all five secrets above for this AppHost; do not put credentials in source control. If authentication fails after a restart, persistent PostgreSQL/RabbitMQ volumes may still contain credentials from an earlier initialization: retain matching secrets or update the existing roles; recreate **only disposable local data**. If `curl` cannot connect, take the API URLs from the current Aspire Resources page instead of assuming fixed ports. See the [architecture setup notes](docs/architecture/README.md) and [scenario runbook](docs/scenarios.md) for further operational details.

## Build, tests and quality gates

```bash
dotnet tool restore
npm ci
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
bash scripts/test-with-coverage.sh
```

The five .NET test suites exercise HTTP, Outbox, Inbox, independent read and contract behavior; PostgreSQL/Testcontainers integration tests need Docker. The PR workflow also verifies architecture boundary regressions, ADR Guard/index, LikeC4 validate/format/build and **at least 80% global line coverage**. Separate CodeQL and Dependency Review workflows check security/dependency changes. For the complete local gate sequence, use [docs/ci.md](docs/ci.md). The [documentation index](docs/README.md) holds the longer implementation and operational details; this README stays a quick-start rather than a tutorial for every framework.
