---
name: ci-workflow-governance
description: "Review or change GitHub Actions and CI validation in this .NET Aspire lab. USE FOR: workflow triggers, permissions, build/test and architecture gates, caching, dependency updates, credentials and CI failures. DO NOT USE FOR: NuGet packaging, library release/versioning policies, or publishing without a dedicated issue."
license: MIT
---

# CI workflow governance for dotnet-observability-lab

Adapted from the source template's ci-release-governance skill. This laboratory currently runs APIs, workers, PostgreSQL and Redis; it has no library NuGet publishing or release.yml policy.

## Before editing
1. Read AGENTS.md, the issue or PR, and list the workflows actually present under .github/workflows/.
2. Inspect existing triggers, path filters, permissions, commands, credentials and artifact lifecycle.
3. Confirm compatibility with global.json (.NET 10), Directory.Packages.props, DotNetObservabilityLab.slnx, ADR Guard and LikeC4.
4. Use authoring-github-workflows for GitHub Actions expression/YAML structural checks.
5. Preserve least-privilege permissions; do not expose secrets to untrusted PR code or persist credentials unnecessarily.
6. Preserve meaningful restore/build/test and architecture validation, including PostgreSQL integration tests when behavior changes warrant them.
7. Do not claim a workflow or gate exists until its file or run is verified.
8. When changed files affect architecture, update LikeC4/ADR documentation under their existing governance rules.

## Validation
Run the exact commands relevant to the change:

```bash
dotnet tool restore
dotnet tool run adr-guard check docs/adr
npm ci
npm run architecture:validate
npm run architecture:format:check
npm run architecture:build
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
dotnet test ./DotNetObservabilityLab.slnx --configuration Release --no-build
```

Verify the affected workflow actually starts and finishes on the PR. Inspect failed job logs rather than inferring success from the YAML parser. Do not add NuGet publishing, tag/release automation, or credentials as part of ordinary CI maintenance.

## Done
Report changed workflow, existing checks preserved, actual run URL/status and any blocked validation.