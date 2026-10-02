#!/usr/bin/env python3
"""Build a conservative Repo2C4 C2 preview model from a fresh Repo2C4 snapshot."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Iterable


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    return parser.parse_args()


def ordered_unique(values: Iterable[str]) -> list[str]:
    return list(dict.fromkeys(value for value in values if value))


def main() -> int:
    args = parse_args()
    snapshot = json.loads(args.snapshot.read_text(encoding="utf-8"))
    evidence = snapshot.get("evidence", [])

    def evidence_ids(
        *,
        prefixes: tuple[str, ...] = (),
        categories: tuple[str, ...] = (),
    ) -> list[str]:
        matches: list[str] = []
        for item in evidence:
            relative_path = item.get("relativePath", "")
            category = item.get("category", "")
            if prefixes and not any(relative_path.startswith(prefix) for prefix in prefixes):
                continue
            if categories and category not in categories:
                continue
            matches.append(item.get("id", ""))
        return ordered_unique(matches)

    def category_ids(*categories: str) -> list[str]:
        return evidence_ids(categories=tuple(categories))

    def project_ids(*prefixes: str) -> list[str]:
        return evidence_ids(
            prefixes=tuple(prefixes),
            categories=(
                "dotnet.project",
                "dotnet.project.kind",
                "dotnet.runtime.http.candidate",
                "dotnet.runtime.worker.candidate",
            ),
        )

    def element(
        element_id: str,
        kind: str,
        name: str,
        *,
        parent_id: str | None = None,
        evidence_ids_value: list[str] | None = None,
        reason: str,
    ) -> dict:
        value = {
            "id": element_id,
            "kind": kind,
            "name": name,
            "evidenceIds": evidence_ids_value or [],
            "status": "requiresReview",
            "reviewReason": reason,
        }
        if parent_id is not None:
            value["parentId"] = parent_id
        return value

    def relation(
        relation_id: str,
        source_id: str,
        destination_id: str,
        description: str,
        *,
        evidence_ids_value: list[str] | None = None,
        reason: str,
    ) -> dict:
        return {
            "id": relation_id,
            "sourceId": source_id,
            "destinationId": destination_id,
            "description": description,
            "evidenceIds": evidence_ids_value or [],
            "status": "requiresReview",
            "reviewReason": reason,
        }

    solution_evidence = category_ids("dotnet.solution.project")
    postgres_evidence = category_ids("dotnet.integration.postgresql.candidate")
    redis_evidence = category_ids("dotnet.integration.redis.candidate")
    rabbitmq_evidence = category_ids("dotnet.integration.rabbitmq.candidate")

    ingestion_api_evidence = project_ids("src/Ingestion.Api/")
    ingestion_outbox_evidence = project_ids("src/Ingestion.Outbox.Worker/")
    consolidation_api_evidence = project_ids("src/Consolidation.Api/")
    consolidation_worker_evidence = project_ids("src/Consolidation.Worker/")
    apphost_evidence = project_ids(
        "src/DotNetObservabilityLab.AppHost/",
        "src/DotNetObservabilityLab.ServiceDefaults/",
    )

    elements = [
        element(
            "el_api_client",
            "actor",
            "API client",
            reason="Retained from the reviewed architecture as an external actor; repository inspection cannot prove human/client boundaries.",
        ),
        element(
            "el_developer",
            "actor",
            "Developer",
            reason="Retained from the reviewed architecture as an external actor; repository inspection cannot prove human/client boundaries.",
        ),
        element(
            "el_lab",
            "softwareSystem",
            "dotnet-observability-lab",
            evidence_ids_value=solution_evidence,
            reason="Repository structure supports a focal system, but the software-system boundary remains a human architectural decision.",
        ),
        element(
            "el_aspire",
            "softwareSystem",
            "Aspire development environment",
            evidence_ids_value=apphost_evidence,
            reason="AppHost and ServiceDefaults are observable in the repository, while treating local Aspire orchestration as a separate software system remains a review decision.",
        ),
        element(
            "el_ingestion_api",
            "container",
            "Ingestion.Api",
            parent_id="el_lab",
            evidence_ids_value=ingestion_api_evidence,
            reason="Executable/HTTP project evidence supports a container candidate; deployment/runtime boundaries remain under review.",
        ),
        element(
            "el_ingestion_outbox_worker",
            "container",
            "Ingestion.Outbox.Worker",
            parent_id="el_lab",
            evidence_ids_value=ingestion_outbox_evidence,
            reason="Worker-host evidence supports a container candidate; deployment/runtime boundaries remain under review.",
        ),
        element(
            "el_consolidation_api",
            "container",
            "Consolidation.Api",
            parent_id="el_lab",
            evidence_ids_value=consolidation_api_evidence,
            reason="Executable/HTTP project evidence supports a container candidate; deployment/runtime boundaries remain under review.",
        ),
        element(
            "el_consolidation_worker",
            "container",
            "Consolidation.Worker",
            parent_id="el_lab",
            evidence_ids_value=consolidation_worker_evidence,
            reason="Worker-host evidence supports a container candidate; deployment/runtime boundaries remain under review.",
        ),
        element(
            "el_postgresql",
            "container",
            "PostgreSQL",
            parent_id="el_lab",
            evidence_ids_value=postgres_evidence,
            reason="PostgreSQL integration evidence is a candidate signal and does not by itself prove ownership, deployment, or runtime communication.",
        ),
        element(
            "el_redis",
            "container",
            "Redis",
            parent_id="el_lab",
            evidence_ids_value=redis_evidence,
            reason="Redis integration evidence is a candidate signal and does not by itself prove ownership, deployment, or runtime communication.",
        ),
        element(
            "el_rabbitmq",
            "container",
            "RabbitMQ",
            parent_id="el_lab",
            evidence_ids_value=rabbitmq_evidence,
            reason="RabbitMQ integration evidence is a candidate signal and does not by itself prove ownership, deployment, or message direction.",
        ),
        element(
            "el_aspire_dashboard",
            "container",
            ".NET Aspire AppHost / Dashboard",
            parent_id="el_aspire",
            evidence_ids_value=apphost_evidence,
            reason="The AppHost is visible in repository evidence, while the orchestration/dashboard runtime boundary is retained from the reviewed model.",
        ),
    ]

    ingestion_postgres = evidence_ids(
        prefixes=("src/Ingestion.Api/", "src/Ingestion.Persistence/"),
        categories=("dotnet.integration.postgresql.candidate",),
    )
    consolidation_postgres = evidence_ids(
        prefixes=("src/Consolidation.Api/", "src/Consolidation.Persistence/"),
        categories=("dotnet.integration.postgresql.candidate",),
    )
    ingestion_redis = evidence_ids(
        prefixes=("src/Ingestion.Api/",),
        categories=("dotnet.integration.redis.candidate",),
    )
    outbox_rabbit = evidence_ids(
        prefixes=("src/Ingestion.Outbox.Worker/",),
        categories=("dotnet.integration.rabbitmq.candidate",),
    )
    consumer_rabbit = evidence_ids(
        prefixes=("src/Consolidation.Worker/",),
        categories=("dotnet.integration.rabbitmq.candidate",),
    )

    relations = [
        relation(
            "rel_api_client_lab",
            "el_api_client",
            "el_lab",
            "Exercises the lab through HTTP",
            reason="The external actor relationship is retained from the reviewed architecture and is not derivable from repository-static evidence.",
        ),
        relation(
            "rel_developer_lab",
            "el_developer",
            "el_lab",
            "Develops and studies the reference implementation",
            reason="The human interaction is retained from the reviewed architecture and is not derivable from repository-static evidence.",
        ),
        relation(
            "rel_developer_aspire",
            "el_developer",
            "el_aspire_dashboard",
            "Runs and inspects the local environment",
            reason="The developer interaction is retained from the reviewed architecture and is not derivable from repository-static evidence.",
        ),
        relation(
            "rel_client_ingestion",
            "el_api_client",
            "el_ingestion_api",
            "POST /values",
            evidence_ids_value=ingestion_api_evidence,
            reason="HTTP-host evidence supports the endpoint container, but the concrete external caller and route remain review assertions.",
        ),
        relation(
            "rel_client_consolidation",
            "el_api_client",
            "el_consolidation_api",
            "GET /consolidated",
            evidence_ids_value=consolidation_api_evidence,
            reason="HTTP-host evidence supports the endpoint container, but the concrete external caller and route remain review assertions.",
        ),
        relation(
            "rel_ingestion_postgresql",
            "el_ingestion_api",
            "el_postgresql",
            "Reads and writes ingestion data",
            evidence_ids_value=ingestion_postgres,
            reason="PostgreSQL package evidence supports an integration candidate; runtime communication and database ownership still require review.",
        ),
        relation(
            "rel_outbox_postgresql",
            "el_ingestion_outbox_worker",
            "el_postgresql",
            "Claims and updates Outbox records",
            reason="The relationship is retained from the reviewed architecture; repository-static project references do not independently prove runtime database communication.",
        ),
        relation(
            "rel_consolidation_api_postgresql",
            "el_consolidation_api",
            "el_postgresql",
            "Reads consolidated data",
            evidence_ids_value=consolidation_postgres,
            reason="PostgreSQL package evidence supports an integration candidate; runtime communication and database ownership still require review.",
        ),
        relation(
            "rel_consolidation_worker_postgresql",
            "el_consolidation_worker",
            "el_postgresql",
            "Commits Inbox and consolidated state",
            reason="The relationship is retained from the reviewed architecture; repository-static project references do not independently prove runtime database communication.",
        ),
        relation(
            "rel_ingestion_redis",
            "el_ingestion_api",
            "el_redis",
            "Uses Redis as an idempotency fast path",
            evidence_ids_value=ingestion_redis,
            reason="Redis package evidence supports an integration candidate; active runtime use remains subject to review.",
        ),
        relation(
            "rel_outbox_rabbitmq",
            "el_ingestion_outbox_worker",
            "el_rabbitmq",
            "Publishes integration events",
            evidence_ids_value=outbox_rabbit,
            reason="RabbitMQ package evidence supports an integration candidate but does not by itself prove publish direction or runtime activity.",
        ),
        relation(
            "rel_rabbitmq_consumer",
            "el_rabbitmq",
            "el_consolidation_worker",
            "Delivers integration events",
            evidence_ids_value=consumer_rabbit,
            reason="RabbitMQ package evidence supports an integration candidate but does not by itself prove consume direction or runtime activity.",
        ),
        relation(
            "rel_apphost_ingestion",
            "el_aspire_dashboard",
            "el_ingestion_api",
            "Orchestrates local process",
            evidence_ids_value=apphost_evidence,
            reason="AppHost evidence exists, while the concrete orchestration relation is retained from the reviewed architecture.",
        ),
        relation(
            "rel_apphost_outbox",
            "el_aspire_dashboard",
            "el_ingestion_outbox_worker",
            "Orchestrates local process",
            evidence_ids_value=apphost_evidence,
            reason="AppHost evidence exists, while the concrete orchestration relation is retained from the reviewed architecture.",
        ),
        relation(
            "rel_apphost_consolidation_api",
            "el_aspire_dashboard",
            "el_consolidation_api",
            "Orchestrates local process",
            evidence_ids_value=apphost_evidence,
            reason="AppHost evidence exists, while the concrete orchestration relation is retained from the reviewed architecture.",
        ),
        relation(
            "rel_apphost_consolidation_worker",
            "el_aspire_dashboard",
            "el_consolidation_worker",
            "Orchestrates local process",
            evidence_ids_value=apphost_evidence,
            reason="AppHost evidence exists, while the concrete orchestration relation is retained from the reviewed architecture.",
        ),
        relation(
            "rel_ingestion_otlp",
            "el_ingestion_api",
            "el_aspire_dashboard",
            "Exports OpenTelemetry telemetry via OTLP",
            reason="OTLP export is retained from the reviewed architecture; the baseline Repo2C4 scanner does not prove this runtime edge.",
        ),
        relation(
            "rel_outbox_otlp",
            "el_ingestion_outbox_worker",
            "el_aspire_dashboard",
            "Exports OpenTelemetry telemetry via OTLP",
            reason="OTLP export is retained from the reviewed architecture; the baseline Repo2C4 scanner does not prove this runtime edge.",
        ),
        relation(
            "rel_consolidation_api_otlp",
            "el_consolidation_api",
            "el_aspire_dashboard",
            "Exports OpenTelemetry telemetry via OTLP",
            reason="OTLP export is retained from the reviewed architecture; the baseline Repo2C4 scanner does not prove this runtime edge.",
        ),
        relation(
            "rel_consolidation_worker_otlp",
            "el_consolidation_worker",
            "el_aspire_dashboard",
            "Exports OpenTelemetry telemetry via OTLP",
            reason="OTLP export is retained from the reviewed architecture; the baseline Repo2C4 scanner does not prove this runtime edge.",
        ),
    ]

    model = {
        "schemaVersion": "1.0",
        "level": "C2",
        "snapshot": snapshot,
        "elements": elements,
        "relations": relations,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(model, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
