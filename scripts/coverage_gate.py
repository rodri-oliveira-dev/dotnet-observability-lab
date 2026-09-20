#!/usr/bin/env python3
"""Enforce repository-wide line coverage across VSTest/Coverlet Cobertura reports.

An assembly can occur in several test-project reports. A (source file, line)
is counted once; it is covered when any suite covers it. Uncovered lines still
contribute to the denominator. This avoids averaging percentages per suite.
"""
import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def source_path(raw: str) -> str | None:
    # Coverlet can emit checkout-absolute, repo-relative OR project-relative
    # filenames. Resolve against files actually present in this checkout.
    path = raw.replace(chr(92), "/")
    if path.startswith("src/") and (ROOT / path).is_file():
        return path
    if "/src/" in path:
        candidate = "src/" + path.rsplit("/src/", 1)[1]
        if (ROOT / candidate).is_file():
            return candidate
    basename = path.rsplit("/", 1)[-1]
    if not basename.endswith(".cs"):
        return None
    candidates = sorted((ROOT / "src").rglob(basename))
    if len(candidates) == 1:
        return candidates[0].relative_to(ROOT).as_posix()
    if len(candidates) > 1:
        matching = [p for p in candidates if path.endswith(p.relative_to(ROOT).as_posix())]
        if len(matching) == 1:
            return matching[0].relative_to(ROOT).as_posix()
        raise ValueError(f"ambiguous coverage source: {raw!r}")
    return None


def collect(reports: list[Path]) -> dict[tuple[str, int], bool]:
    lines: dict[tuple[str, int], bool] = {}
    for report in reports:
        root = ET.parse(report).getroot()
        if root.tag != "coverage":
            raise ValueError(f"{report} is not a Cobertura report")
        for element in root.findall(".//class"):
            filename = source_path(element.get("filename", ""))
            if filename is None:
                continue
            for line in element.findall("./lines/line"):
                number = int(line.attrib["number"])
                hits = int(line.attrib["hits"])
                key = (filename, number)
                lines[key] = lines.get(key, False) or hits > 0
    return lines


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, required=True)
    parser.add_argument("--minimum", type=int, default=80)
    parser.add_argument("projects", nargs="+", help="Every required test-project report")
    args = parser.parse_args()
    if not 0 <= args.minimum <= 100:
        parser.error("minimum must be between 0 and 100")
    reports: list[Path] = []
    for project in args.projects:
        found = sorted((args.results / project).rglob("coverage.cobertura.xml"))
        if len(found) != 1:
            print(f"ERROR: expected exactly one Cobertura report for {project}, found {len(found)}", file=sys.stderr)
            return 1
        reports.extend(found)
    try:
        lines = collect(reports)
    except (ET.ParseError, OSError, KeyError, ValueError) as exc:
        print(f"ERROR: invalid coverage data: {exc}", file=sys.stderr)
        return 1
    if not lines:
        print("ERROR: no repository production lines were instrumented", file=sys.stderr)
        return 1
    # An artificially small subset can report 100% while ignoring application
    # assemblies. Require the principal production flows in the aggregate.
    required = {
        "src/Ingestion.Api/Values/IngestValueHandler.cs",
        "src/Ingestion.Outbox.Worker/OutboxProcessor.cs",
        "src/Consolidation.Worker/ConsolidationProcessor.cs",
        "src/Consolidation.Api/Consolidated/ConsolidatedQuery.cs",
    }
    missing = required - {file for file, _ in lines}
    if missing:
        print(f"ERROR: missing production coverage sources: {sorted(missing)}", file=sys.stderr)
        return 1
    covered = sum(lines.values())
    total = len(lines)
    percent = covered * 100 / total
    print(f"Global line coverage: {covered}/{total} = {percent:.2f}% (minimum {args.minimum}%)")
    if covered * 100 < args.minimum * total:
        print("ERROR: global line coverage is below the required threshold", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
