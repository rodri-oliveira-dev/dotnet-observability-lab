# dotnet-observability-lab

A small reference implementation for distributed observability with .NET Aspire and OpenTelemetry.

The repository intentionally keeps the business domain simple so that asynchronous reliability, idempotency, tracing, metrics, logs, ADRs, and C4 documentation remain easy to understand.

## Current scope

Issue #1 establishes the executable topology only:

- `Ingestion.Api`
- `Ingestion.Outbox.Worker`
- `Consolidation.Api`
- `Consolidation.Worker`
- `DotNetObservabilityLab.AppHost`
- `DotNetObservabilityLab.ServiceDefaults`
- `Contracts`

PostgreSQL, Redis, RabbitMQ, Outbox/Inbox processing, and application-level telemetry are introduced by later roadmap issues.

## Architecture style

The project uses a **lightweight Vertical Slice Architecture with explicit infrastructure boundaries**.

It intentionally does not use a full Clean Architecture project-per-layer structure. New abstractions should represent real boundaries or variation points, not architectural ceremony.

Core boundary rules:

- the two APIs never call each other;
- `Consolidation.*` must not depend on `Ingestion.*`;
- `Ingestion.*` must not depend on `Consolidation.*`;
- ServiceDefaults contains only cross-cutting Aspire defaults;
- integration contracts belong in `Contracts`, not in either service's domain model.

## Prerequisites

- .NET 10 SDK (the repository pins `10.0.400` and rolls forward to the latest feature band)
- Aspire CLI compatible with Aspire 13.5
- an OCI-compatible container runtime will be required once infrastructure resources are added in later issues

## Restore and build

```bash
dotnet tool restore
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
```

## Run

Start the AppHost:

```bash
aspire run --project ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

The Aspire Dashboard should show four application resources:

- `ingestion-api`
- `ingestion-outbox-worker`
- `consolidation-api`
- `consolidation-worker`

Both APIs expose a minimal root endpoint and the Aspire development health endpoints.

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
