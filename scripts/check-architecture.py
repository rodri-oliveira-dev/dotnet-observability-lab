#!/usr/bin/env python3
"""Reject cross-boundary project references and transport imports in business code."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
PROJECTS = {p.parent.name: p for p in SRC.glob("*/*.csproj")}
errors: list[str] = []


def boundary(name: str) -> str | None:
    return name.split(".", 1)[0] if name.startswith(("Ingestion.", "Consolidation.")) else None


graph: dict[str, list[str]] = {}
for name, project in sorted(PROJECTS.items()):
    root = ET.parse(project).getroot()
    references = []
    for item in root.findall(".//ProjectReference"):
        target = Path(item.attrib["Include"].replace("\\", "/")).stem
        if target not in PROJECTS:
            errors.append(f"{project.relative_to(ROOT)}: unknown project reference {target}")
        references.append(target)
    graph[name] = references


def visit(origin: str, current: str, seen: set[str]) -> None:
    for target in graph.get(current, []):
        if target in seen:
            continue
        seen.add(target)
        owner = boundary(origin)
        dependency = boundary(target)
        if owner and dependency and owner != dependency:
            errors.append(f"{origin} must not depend on {target} (via {current})")
        if origin == "Contracts" and dependency:
            errors.append(f"Contracts must not depend on {target} (via {current})")
        visit(origin, target, seen)


for project in sorted(PROJECTS):
    visit(project, project, {project})

# These are business/data/contract sources, not the intentional RabbitMQ
# adapters or Aspire composition roots. Keep transport types out of them.
business_sources = [
    *SRC.glob("Contracts/**/*.cs"),
    *SRC.glob("*.Persistence/**/*.cs"),
    *SRC.glob("Ingestion.Api/Values/**/*.cs"),
    *SRC.glob("Consolidation.Api/Consolidated/**/*.cs"),
    SRC / "Consolidation.Worker/ConsolidationProcessor.cs",
    SRC / "Ingestion.Outbox.Worker/OutboxProcessor.cs",
]
for source in business_sources:
    if not source.is_file() or "Migrations" in source.parts:
        continue
    for number, line in enumerate(source.read_text(encoding="utf-8").splitlines(), 1):
        if re.search(r"^\\s*(?:global\\s+)?using\\s+(?:static\\s+)?(?:RabbitMQ\\.Client|Aspire)(?:\\.|\\s*;)", line):
            errors.append(f"{source.relative_to(ROOT)}:{number}: transport/composition import in business code")

if errors:
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    sys.exit(1)
print(f"Architecture boundary checks passed ({len(PROJECTS)} production projects).")
