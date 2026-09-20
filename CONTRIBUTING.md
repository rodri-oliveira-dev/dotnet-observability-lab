# Contributing

Choose the [English README](README.md) or the [README em Português (Brasil)](README.pt-BR.md) for the same architecture overview, local startup commands and testing prerequisites. Keep both README versions in sync when onboarding or architecture references change.

Keep changes small, reproducible, and proportional to this lab's purpose.

## Environment

- Use the .NET SDK defined by `global.json`.
- Restore repository-local tools with `dotnet tool restore`.
- Use Node.js 20+ and install architecture tooling with `npm ci`.
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

LikeC4 is the source of truth for architecture diagrams. When a change modifies an architectural relationship, process, responsibility, database/cache ownership, queue/broker relationship, or observability flow represented by the model, update `docs/architecture` in the same pull request.

Use ADR Guard for architectural decisions. Create/update an ADR when a change introduces or materially changes a decision with plausible alternatives and meaningful consequences. Do not create ADRs for routine implementation details.

## Validation

Run validation proportional to the change. The architecture/documentation baseline is:

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

If ADRs changed, regenerate and commit the deterministic index:

```bash
dotnet tool run adr-guard index docs/adr
```

Tests and coverage are introduced as behavior is added. Do not create tests for framework internals merely to increase coverage.

## CI-equivalent local checks

After the Release build, run `python3 scripts/check-architecture.py` and `bash scripts/test-with-coverage.sh` to exercise the five existing test suites and enforce the global 80% line-coverage threshold. PostgreSQL integration tests require a Docker-compatible engine. Keep ADR Guard, ADR index and LikeC4 checks in the validation sequence above.
