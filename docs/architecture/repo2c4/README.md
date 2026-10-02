# Repo2C4 preview

This directory documents the parallel Repo2C4 migration preview. The current hand-maintained LikeC4 model in the parent directory remains the authoritative architecture until the preview has been reviewed and an explicit cutover is made.

The architecture workflow performs the preview from a clean checkout:

1. restore the pinned local `Repo2C4.Cli` tool;
2. run `repo2c4 inspect --repository .` to create a fresh evidence snapshot;
3. build a conservative C2 `ArchitectureModel` from that exact snapshot;
4. run `repo2c4 generate --apply` into an isolated workflow workspace;
5. run `repo2c4 validate` using the repository's pinned LikeC4 CLI;
6. publish the generated interactive site under `/repo2c4/` while keeping the existing Pages root unchanged.

All preview elements and relations are intentionally emitted as `requiresReview`. Repository-static evidence can support candidates, but it does not automatically prove deployment boundaries, runtime communication, message direction, or human/external-system relationships.

Generated `.c4` files and `evidence-report.md` are workflow outputs rather than committed sources so they cannot be recursively loaded by the existing `docs/architecture` LikeC4 workspace. The workflow artifact contains the generated files for side-by-side review.

After merge, compare:

- current documentation: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/>
- Repo2C4 preview: <https://rodri-oliveira-dev.github.io/dotnet-observability-lab/repo2c4/>

No existing C1, C2, C3, dynamic view, Mermaid export, or GitHub Pages root is removed by this preview.
