# Contributing

Keep changes small, reproducible, and proportional to this lab's purpose.

## Environment

- Use the .NET SDK defined by `global.json`.
- Restore repository-local tools with `dotnet tool restore`.
- Package versions belong in `Directory.Packages.props`.
- Never commit secrets, tokens, passwords, or local environment files.

## Branches and commits

- Do not work directly on `main`.
- Use short-lived branches scoped to one issue or objective.
- Use Conventional Commits: `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`, or `ci:`.

## Architecture

This repository uses lightweight Vertical Slices, not full Clean Architecture.

Preserve the runtime boundaries:

- `Ingestion.*` and `Consolidation.*` do not reference each other;
- APIs own HTTP concerns;
- workers own continuous background processing;
- transport-specific details stay at infrastructure/process boundaries;
- ServiceDefaults contains cross-cutting Aspire defaults only.

When a change modifies an architectural decision, relationship, component, database, queue, or observability flow, update the corresponding ADR/LikeC4 documentation once those facilities are introduced.

## Validation

Run validation proportional to the change. The baseline is:

```bash
dotnet tool restore
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
```

Tests and coverage are introduced as behavior is added. Do not create tests for framework internals merely to increase coverage.
