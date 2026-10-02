# Repo2C4 preview

This directory documents the parallel Repo2C4 migration preview. The current hand-maintained LikeC4 model in the parent directory remains the authoritative architecture until the preview has been reviewed and an explicit cutover is made.

The architecture workflow performs the preview from a clean checkout:

1. restore the pinned local `Repo2C4.Cli` tool;
2. run `repo2c4 inspect --repository .` to create a fresh evidence snapshot;
3. build a conservative C2 `ArchitectureModel` from that exact snapshot;
4. generate and validate the base C1/C2 workspace;
5. use the same C2 model to generate one selective C3 workspace for each executable application container;
6. validate every generated workspace with the repository's pinned LikeC4 CLI;
7. publish the generated interactive sites under `/repo2c4/` while keeping the existing Pages root unchanged.

Repo2C4 1.1.0 supports one selected C3 container per generation. The preview therefore publishes four independent Level 3 workspaces from the same snapshot and C2 model:

- Ingestion.Api: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/c3/ingestion-api/>
- Ingestion.Outbox.Worker: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/c3/ingestion-outbox-worker/>
- Consolidation.Api: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/c3/consolidation-api/>
- Consolidation.Worker: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/c3/consolidation-worker/>

All preview elements, relations, and generated C3 component boundaries are intentionally emitted as `requiresReview`. Repository-static evidence can support candidates and responsibility groupings, but it does not automatically prove deployment boundaries, runtime communication, message direction, or final component boundaries.

Generated `.c4` files and `evidence-report.md` are workflow outputs rather than committed sources so they cannot be recursively loaded by the existing `docs/architecture` LikeC4 workspace. The workflow artifact contains the C2 and all four C3 generated workspaces for side-by-side review.

After merge, compare:

- current documentation: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/>
- Repo2C4 C1/C2 preview: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/>
- Repo2C4 Level 3 previews: the four URLs above.

No existing C1, C2, C3, dynamic view, Mermaid export, or GitHub Pages root is removed by this preview.
