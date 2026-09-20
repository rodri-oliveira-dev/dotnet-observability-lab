#!/usr/bin/env python3
"""Guard project boundaries and keep transport/composition types out of business code."""
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


# Contracts and persistence contain reusable business/data types, not broker or
# Aspire composition. Check their project references as well as their source.
pure_projects = {
    name for name in PROJECTS
    if name == "Contracts" or name.endswith(".Persistence")
}
infrastructure_projects = {"Messaging", "DotNetObservabilityLab.ServiceDefaults", "DotNetObservabilityLab.AppHost"}
forbidden_packages = ("RabbitMQ.Client", "Aspire.")

graph: dict[str, list[str]] = {}
for name, project in sorted(PROJECTS.items()):
    root = ET.parse(project).getroot()
    references: list[str] = []
    for item in root.findall(".//ProjectReference"):
        target = Path(item.attrib["Include"].replace("\\", "/")).stem
        if target not in PROJECTS:
            errors.append(f"{project.relative_to(ROOT)}: unknown project reference {target}")
        references.append(target)
    graph[name] = references
    if name in pure_projects:
        for item in root.findall(".//PackageReference"):
            package = item.attrib.get("Include", item.attrib.get("Update", ""))
            if package == forbidden_packages[0] or package.startswith(forbidden_packages[1]):
                errors.append(f"{name}: forbidden transport/composition package {package}")


def visit(origin: str, current: str, seen: set[str]) -> None:
    for target in graph.get(current, []):
        if target in seen:
            continue
        seen.add(target)
        owner, dependency = boundary(origin), boundary(target)
        if owner and dependency and owner != dependency:
            errors.append(f"{origin} must not depend on {target} (via {current})")
        if origin == "Contracts" and dependency:
            errors.append(f"Contracts must not depend on {target} (via {current})")
        if origin in pure_projects and target in infrastructure_projects:
            errors.append(f"{origin} must not depend on transport/composition project {target} (via {current})")
        visit(origin, target, seen)


for project in sorted(PROJECTS):
    visit(project, project, {project})

# Check all business/data sources, not only line-prefix 'using' statements:
# aliases, global usings and fully qualified references are prohibited too.
business_sources: set[Path] = set()
for name in pure_projects:
    business_sources.update(PROJECTS[name].parent.rglob("*.cs"))
business_sources.update((SRC / "Ingestion.Api/Values").rglob("*.cs"))
business_sources.update((SRC / "Consolidation.Api/Consolidated").rglob("*.cs"))
business_sources.update([
    SRC / "Consolidation.Worker/ConsolidationProcessor.cs",
    SRC / "Ingestion.Outbox.Worker/OutboxProcessor.cs",
])

# Ignore non-code trivia so comments/URLs/doc examples cannot trigger a false positive.
trivia = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/')
transport_type = re.compile(r'(?<![\w.])(?:global::)?(?:RabbitMQ\s*\.\s*Client\b|Aspire\s*\.)')
for source in sorted(business_sources):
    if not source.is_file() or any(part in {"obj", "bin", "Migrations"} for part in source.parts):
        continue
    content = source.read_text(encoding="utf-8")
    code = trivia.sub(lambda m: "\n" * m.group(0).count("\n"), content)
    match = transport_type.search(code)
    if match:
        line = code.count("\n", 0, match.start()) + 1
        errors.append(f"{source.relative_to(ROOT)}:{line}: transport/composition type in business code")

if not PROJECTS:
    errors.append("No production projects found")
if errors:
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    sys.exit(1)
print(f"Architecture boundary checks passed ({len(PROJECTS)} production projects).")

