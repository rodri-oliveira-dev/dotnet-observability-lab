# Architecture

LikeC4 is the source of truth for architecture diagrams in this repository.

The model is intentionally small and follows the same principle as the implementation: document only distinctions that help explain ownership, runtime boundaries, reliability, and observability.

## Architecture style

The implementation style is:

> **Lightweight Vertical Slice Architecture with explicit infrastructure boundaries.**

The repository deliberately avoids a full Clean Architecture project-per-layer structure. Architecture should remain proportional to the small application while making the important boundaries explicit.

The two persistence projects are boundary-internal implementation libraries shared by each boundary's API and Worker. They exist to keep one authoritative `DbContext`, mappings, and migration history per logical database; they are not a general project-per-layer architecture.

## C4 levels

The repository uses three C4 levels.

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

| Boundary | Database | Allowed processes |
| --- | --- | --- |
| Ingestion | `ingestion_db` | `Ingestion.Api`, `Ingestion.Outbox.Worker` |
| Consolidation | `consolidation_db` | `Consolidation.Api`, `Consolidation.Worker` |

No process receives the other boundary's database reference.

Redis is also a current Aspire resource, but it is deliberately wired only to:

- `Ingestion.Api` for the later HTTP idempotency fast path;
- `Consolidation.Worker` for the later duplicate-message fast path.

Redis is not persistent correctness state. PostgreSQL remains authoritative.

RabbitMQ remains a **future placeholder** until its dedicated implementation issue.

The `Contracts` and `*.Persistence` projects are not shown as runtime containers because they are code/build dependencies, not separately running processes.

### Level 3 — Component

Answers:

> How is one application boundary organized internally around its meaningful responsibilities?

Component views are intentionally deferred until the corresponding behavior exists. Later issues add component views for ingestion, Outbox publication, consolidation, and read-side querying.

Do not create a component for every class. A component should represent a meaningful responsibility, boundary, port, adapter, hosted service, or processing stage.

## Database ownership

The local environment deliberately uses one PostgreSQL container for operational simplicity, but that does not create a shared database model.

Rules:

- `ingestion_db` is owned only by the ingestion boundary;
- `consolidation_db` is owned only by the consolidation boundary;
- no cross-database queries;
- no shared tables;
- each boundary owns its EF Core mappings and migrations;
- schema changes create new migrations rather than rewriting migration history.

The local single-server topology is not a production deployment decision.

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

Runtime applications do not use those environment variables; Aspire injects the owned database references.

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

The OpenTelemetry architectural decision and end-to-end propagation are introduced by their dedicated roadmap issue.

## Files

- `model.c4` — element definitions, ownership, descriptions, and relationships.
- `views.c4` — C4 views and the small shared visual convention.

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
- Later runbooks/scenario documentation explains how to execute and troubleshoot the system.

An ADR is historical decision context; LikeC4 is the architecture model that should remain synchronized with the implementation.
