# Architecture

LikeC4 is the source of truth for architecture diagrams in this repository.

The model is intentionally small and follows the same principle as the implementation: document only distinctions that help explain ownership, runtime boundaries, reliability, and observability.

## Architecture style

The implementation style is:

> **Lightweight Vertical Slice Architecture with explicit infrastructure boundaries.**

The repository deliberately avoids a full Clean Architecture project-per-layer structure. Architecture should remain proportional to the small application while making the important boundaries explicit.

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

The four implemented processes are:

- `Ingestion.Api`
- `Ingestion.Outbox.Worker`
- `Consolidation.Api`
- `Consolidation.Worker`

PostgreSQL, Redis, and RabbitMQ are present as **future placeholders** because the roadmap already depends on those architectural boundaries. They are styled as planned and must be updated by the issue that actually introduces each resource.

The `Contracts` project is not shown as a runtime container because it is a code/build dependency, not a separately running process.

### Level 3 — Component

Answers:

> How is one application boundary organized internally around its meaningful responsibilities?

Component views are intentionally deferred until the corresponding behavior exists. Later issues add component views for ingestion, Outbox publication, consolidation, and read-side querying.

Do not create a component for every class. A component should represent a meaningful responsibility, boundary, port, adapter, hosted service, or processing stage.

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
