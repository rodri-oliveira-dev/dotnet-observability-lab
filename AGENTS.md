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
11. `.agents/skills/` (only the task-relevant skill) and `.agents/SOURCES.md` for provenance.

Do not load or copy unrelated material merely because another repository contains it.


## Context and agent skill routing

- Start with the issue or PR and the smallest relevant set of source, test, configuration and architecture files. Search symbols before loading large files.
- Do not load unrelated skills or generated outputs by default. Read the selected skill's `.agents/skills/<name>/SKILL.md` only when its trigger matches the task.
- The repository-local skills are operational playbooks, not additional frameworks or permission to expand scope. `AGENTS.md`, the current issue, actual repository files, and deterministic validation override generic examples inside a skill.
- Do not assume GitHub workflows, release mechanisms, coverage collectors, OIDC configuration, scanners or auxiliary tools exist until their actual files/configuration have been inspected.
- If subagents are available, delegate mechanical inventory and searches, not decisions about data correctness, concurrency, security or architecture. Review all delegated edits under the same quality gates.
- For any write-side change, consider atomicity, durable idempotency, Outbox state and integration contract compatibility. For read-side work, preserve independence from the write API and ingestion database.
- For integration tests, exercise real PostgreSQL semantics with the existing Testcontainers setup when database transactions, uniqueness or concurrency matter; never replace those assertions with a mock-only happy path.

## Local skills

Use the minimum relevant skill or complementary pair. These files are selectively adapted from
[dotnet-library-template](https://github.com/rodri-oliveira-dev/dotnet-library-template/tree/main/.agents);
this repository is a distributed application, **not** a library template.

| Skill | Use when |
| --- | --- |
| `dotnet-issue-implementation` | Implementing a defined issue, including acceptance criteria and proof of completion |
| `dotnet-bug-investigation` | Investigating unexpected behavior, failed tests or a regression before fixing it |
| `dotnet-pr-review` | Reviewing a PR/diff for correctness, risks, regressions and relevant test coverage |
| `dotnet-security-review` | Reviewing HTTP, messaging, persistence, configuration, secrets or dependency security |
| `dotnet-refactoring-engineer` | Focused refactoring with unchanged observable behavior and integration contracts |
| `coverage-analysis` | Analyzing existing coverage reports and prioritizing gaps by behavioral risk |
| `test-gap-analysis` | Finding publicly observable, unprotected behaviors and focused regression tests |
| `test-anti-patterns` | Auditing weak assertions, flaky tests, over-mocking and order dependencies |
| `ci-workflow-governance` | Reviewing existing CI build, test, architecture checks, triggers and permissions |
| `authoring-github-workflows` | Validating GitHub Actions YAML and expressions; pair with CI governance when needed |
| `directory-build-organization` | Changing MSBuild props/targets or Central Package Management with correct import order |
| `binlog-failure-analysis` | Diagnosing an opaque MSBuild error where binary-log evidence adds value |

Do not apply NuGet library packaging, template generation, release versioning, BenchmarkDotNet
or trusted-publishing guidance from the source project. Their supporting skills were deliberately
not imported because the lab has no such workflows or projects in its current scope. Do not
introduce new tooling just to satisfy a skill's optional example. Attribution, source revisions
and selected supporting references are recorded in `.agents/SOURCES.md`.

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


For changes limited to `.agents/` or `AGENTS.md`, verify skill metadata, file links, source
attribution, routing consistency and the absence of copied library-template commands. If
production code or workflows also change, run the corresponding .NET, ADR, LikeC4 and
integration-test gates; a prior green run is not evidence for a new code change.

When reviewing GitHub Actions files, inspect the actual workflows first and use
`actionlint` when available. CI checks are defined by the workflows under
`.github/workflows/`: ingestion build/integration tests/ADR Guard/LikeC4, CodeQL C#
analysis and pull-request Dependency Review. The workflow pin-policy check is a
structural safeguard, **not** a substitute for actionlint or runtime execution.
Dependabot covers NuGet, .NET SDK, npm/LikeC4 and GitHub Actions through
`.github/dependabot.yml`. Do not assume SonarQube or NuGet release automation exists.


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
