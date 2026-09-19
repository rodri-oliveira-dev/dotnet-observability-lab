# Agent skill sources — dotnet-observability-lab

The listed skills are selected, locally adapted copies from
[rodri-oliveira-dev/dotnet-library-template](https://github.com/rodri-oliveira-dev/dotnet-library-template/tree/2e0853d367188569993bfbe840a8578f6a61912e/.agents),
at immutable source revision `2e0853d367188569993bfbe840a8578f6a61912e`. Import is manual, not an automatic synchronization.
`AGENTS.md` and actual repository state override template-specific examples.

| Local skill | Source file | Classification | Adaptation |
| --- | --- | --- | --- |
| [authoring-github-workflows](skills/authoring-github-workflows/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/authoring-github-workflows/SKILL.md) | upstream-adapted | GitHub Actions YAML and expression validation; removed nonexistent agent-governance gate and template-only assumptions. |
| [binlog-failure-analysis](skills/binlog-failure-analysis/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/binlog-failure-analysis/SKILL.md) | upstream-adapted | MSBuild triage in the .slnx repository; quoted semicolon-separated logger arguments. |
| [ci-workflow-governance](skills/ci-workflow-governance/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/ci-release-governance/SKILL.md) | internal-adapted | Adapted ci-release-governance to actual build/test/ADR/LikeC4 workflows; removed NuGet, tag and release assumptions. |
| [coverage-analysis](skills/coverage-analysis/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/coverage-analysis/SKILL.md) | upstream-adapted | Coverage is advisory until a collector is configured; no fictitious xUnit v3/MTP or mandatory Coverlet invocation. |
| [directory-build-organization](skills/directory-build-organization/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/directory-build-organization/SKILL.md) | upstream-adapted | Preserve existing .NET 10 and Central Package Management; includes relevant MSBuild references. |
| [dotnet-bug-investigation](skills/dotnet-bug-investigation/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/dotnet-bug-investigation/SKILL.md) | local-adapted | Service/API and worker regressions; evidence and real persistence behavior. |
| [dotnet-issue-implementation](skills/dotnet-issue-implementation/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/dotnet-issue-implementation/SKILL.md) | local-adapted | Issue DoD, integration contracts, LikeC4/ADR and repository validation rather than library packaging. |
| [dotnet-pr-review](skills/dotnet-pr-review/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/dotnet-pr-review/SKILL.md) | local-adapted | Review API/event compatibility, idempotency, worker/database boundaries and CI evidence. |
| [dotnet-refactoring-engineer](skills/dotnet-refactoring-engineer/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/dotnet-refactoring-engineer/SKILL.md) | internal-adapted | Behavior-preserving service refactor without unrelated library packaging. |
| [dotnet-security-review](skills/dotnet-security-review/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/dotnet-security-review/SKILL.md) | local-adapted | Threat-based review of HTTP, messaging, database roles, secrets and actual available security gates. |
| [test-anti-patterns](skills/test-anti-patterns/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/test-anti-patterns/SKILL.md) | upstream-adapted | Existing xUnit/Testcontainers baseline instead of xUnit v3/MTP/AwesomeAssertions/NSubstitute. |
| [test-gap-analysis](skills/test-gap-analysis/SKILL.md) | [source](https://github.com/rodri-oliveira-dev/dotnet-library-template/blob/2e0853d367188569993bfbe840a8578f6a61912e/.agents/skills/test-gap-analysis/SKILL.md) | upstream-adapted | Observable service/API and asynchronous correctness gaps; carries on-demand mutation catalog. |

Supporting references copied for `directory-build-organization` (three MSBuild references)
and `test-gap-analysis` (`mutation-catalog.md`) from the same immutable source revision.

Third-party-derived skills preserve MIT licensing and .NET Foundation attribution in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Full provenance before this local
adaptation remains in the source repository's `.agents/SOURCES.md`.

Excluded deliberately: `dotnet-library-change` (library/package semantics overlap
issue implementation), `microbenchmarking` (no BenchmarkDotNet suite), and
`nuget-trusted-publishing` (no NuGet publication in this lab).

Update policy: compare source changes manually; keep task-specific edits minimal.
When changing imported upstream content, update this manifest, `AGENTS.md` routing
where necessary, and the notices if licensing/attribution changes. Do not copy
generated templates, packaging scripts, lock-file assumptions, or nonexistent CI gates.
