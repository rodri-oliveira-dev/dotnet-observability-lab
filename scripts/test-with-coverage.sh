#!/usr/bin/env bash
set -euo pipefail

# Run from any working directory. Restore/build the solution before invoking this script.
cd "$(dirname "${BASH_SOURCE[0]}")/.."
results="TestResults/coverage"
rm -rf -- "$results"

projects=(
  Ingestion.Api.Tests
  Ingestion.Outbox.Worker.Tests
  Consolidation.Worker.Tests
  Consolidation.Api.Tests
  Messaging.Contracts.Tests
)

for project in "${projects[@]}"; do
  dotnet test "tests/$project/$project.csproj" \
    --configuration Release --no-build --no-restore \
    --collect:"XPlat Code Coverage" --settings ./coverlet.runsettings \
    --results-directory "$results/$project"
done

python3 scripts/coverage_gate.py --results "$results" --minimum 80 "${projects[@]}"
