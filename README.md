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
- Redis as an auxiliary optimization resource
- `DotNetObservabilityLab.AppHost`
- `DotNetObservabilityLab.ServiceDefaults`
- `Contracts`
- ADR governance with ADR Guard
- architecture-as-code with LikeC4

RabbitMQ, HTTP idempotency behavior, Outbox/Inbox processing, and application-level telemetry are introduced by later roadmap issues.

## Architecture style

The project uses a **lightweight Vertical Slice Architecture with explicit infrastructure boundaries**.

It intentionally does not use a full Clean Architecture project-per-layer structure. New abstractions should represent real boundaries or variation points, not architectural ceremony.

Core boundary rules:

- the two APIs never call each other;
- `Consolidation.*` must not depend on `Ingestion.*`;
- `Ingestion.*` must not depend on `Consolidation.*`;
- `Ingestion.*` can access only `ingestion_db`;
- `Consolidation.*` can access only `consolidation_db`;
- Redis is an optimization, never the durable source of truth;
- ServiceDefaults contains only cross-cutting Aspire defaults;
- integration contracts belong in `Contracts`, not in either service's domain model.

Architecture documentation lives under [docs](docs/README.md). The LikeC4 model is the source of truth for diagrams, and ADR Guard validates architectural decisions.

## Prerequisites

- .NET 10 SDK (the repository pins `10.0.400` and rolls forward to the latest feature band)
- Aspire CLI compatible with Aspire 13.5
- Node.js 20+ for LikeC4 architecture tooling
- an OCI-compatible container runtime for PostgreSQL and Redis

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

The APIs and workers receive only their owned PostgreSQL database reference. Redis is referenced only by `Ingestion.Api` and `Consolidation.Worker`.

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
