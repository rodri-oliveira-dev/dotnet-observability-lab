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
  mkdir -p "$results/$project"
  dotnet test "tests/$project/$project.csproj" \
    --configuration Release --no-build --no-restore \
    -p:CollectCoverage=true \
    -p:CoverletOutput="$PWD/$results/$project/" \
    -p:CoverletOutputFormat="cobertura%2copencover" \
    -p:ExcludeByAttribute="GeneratedCodeAttribute%2cExcludeFromCodeCoverageAttribute" \
    -p:ExcludeByFile="**/Program.cs%2c**/Migrations/**%2c**/*DbContextFactory.cs%2c**/obj/**"
done

python3 scripts/coverage_gate.py --results "$results" --minimum 80 "${projects[@]}"
