# AGENTS.md

## Purpose

This repository is a small .NET reference implementation for distributed observability, reliable asynchronous processing, and idempotency using .NET Aspire and OpenTelemetry.

Agent-driven changes must remain small, correct, reproducible, and aligned with the issue currently being implemented.

## Sources of truth

Read only the material relevant to the current task, starting with:

1. the current GitHub issue;
2. `README.md`;
3. `CONTRIBUTING.md`;
4. `docs/adr/`;
5. `docs/architecture/`;
6. `Directory.Packages.props`;
7. `Directory.Build.props`;
8. `.editorconfig`;
9. `global.json`;
10. `DotNetObservabilityLab.slnx`.

Do not load or copy unrelated material merely because another repository contains it.

## Architecture

The agreed style is **lightweight Vertical Slice Architecture with explicit infrastructure boundaries**.

Do not convert the project into a full Clean Architecture project-per-layer structure.

Runtime boundaries:

- `Ingestion.Api` owns the write HTTP surface.
- `Ingestion.Outbox.Worker` owns continuous Outbox publication.
- `Consolidation.Worker` owns message consumption and read-model consolidation.
- `Consolidation.Api` owns the read HTTP surface.
- `Contracts` contains only true integration contracts.
- `DotNetObservabilityLab.ServiceDefaults` contains Aspire cross-cutting defaults only.

Mandatory dependency rules:

- `Consolidation.*` must not reference `Ingestion.*`.
- `Ingestion.*` must not reference `Consolidation.*`.
- Domain/application code must not depend on Aspire.
- Transport concerns must not leak into domain logic.

## Architecture documentation

- LikeC4 under `docs/architecture` is the source of truth for architecture diagrams.
- ADRs under `docs/adr` record significant architectural decisions and are validated with ADR Guard.
- A change that alters a modeled process, responsibility, relationship, data owner, broker/cache/database boundary, or observability relationship must update LikeC4 in the same change.
- A significant architectural decision with plausible alternatives and consequences requires a new or updated ADR.
- Do not rewrite historical ADRs merely to make them look current; supersede decisions according to the ADR process.
- Do not duplicate LikeC4 diagrams by hand in Markdown.

## Implementation rules

- Make the smallest change that satisfies the issue.
- Do not add an abstraction because it is a generic best practice.
- Add interfaces only for real architectural boundaries or meaningful alternative implementations.
- Do not introduce MediatR, generic repositories, generic Unit of Work abstractions, or internal frameworks without an explicit requirement.
- Do not invent URLs, ports, contracts, infrastructure, or behavior.
- Do not add PostgreSQL, Redis, RabbitMQ, or other resources before the issue that introduces them.
- Do not introduce secrets.
- Do not modify tests merely to make them pass.
- Remove abandoned empty/whitespace-only files before finishing.
- Preserve observable behavior during refactoring unless the issue explicitly requests a behavior change.

## Time and determinism

When production behavior requires current time, timestamps, polling delays, retry scheduling, or lock expiry:

- inject .NET `TimeProvider` directly;
- register `TimeProvider.System` in the relevant composition root;
- replace it with a deterministic provider in tests when needed;
- do not introduce a custom `IClock` abstraction.

Do not inject `TimeProvider` into components that do not need time.

## Packages

- The repository uses Central Package Management.
- Never put a `Version=` on a `PackageReference`.
- Keep `Directory.Packages.props` limited to packages actually used by the lab.
- Node architecture tooling is pinned in `package.json` and `package-lock.json`.
- Evaluate new dependencies for concrete value, operational cost, and lock-in before adding them.

## Validation

Run validation proportional to the impact.

Architecture/documentation baseline:

```bash
dotnet tool restore
dotnet tool run adr-guard check docs/adr

npm ci
npm run architecture:validate
npm run architecture:format:check
npm run architecture:build

dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
```

When ADRs change, regenerate `docs/adr/README.md` with:

```bash
dotnet tool run adr-guard index docs/adr
```

Run the closest relevant tests once test projects exist. Record any validation that cannot be executed and why.

## Git

- Never implement directly on `main`.
- Use a branch related to the issue/objective.
- Review the diff before committing.
- Use Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, `ci:`.
- Do not mix unrelated refactors with functional changes.
