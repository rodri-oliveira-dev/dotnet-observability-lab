#!/usr/bin/env python3
"""Opt-in, local-only evidence collector for the six live Aspire demonstrations.

This is not a CI test, an Aspire Dashboard client, or a claim that a scenario
passed. A scenario is PASS only when its observed HTTP/SQL assertions succeed
AND a human attaches real Dashboard traces/logs/metrics to the JSON record.
Run only with disposable development data. Do not upload raw evidence.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from decimal import Decimal

from runtime_http_smoke import exchange, expect_total, git_sha, snapshot

SCENARIOS = {
    1: "Accepted write, durable Outbox and independent read",
    2: "Duplicate HTTP idempotency",
    3: "Duplicate AMQP MessageId",
    4: "Ingestion API stopped; independent read and Outbox",
    5: "RabbitMQ outage and recovery",
    6: "Reversible consolidation failure and recovery",
}
SQL = {
    "ingestion": """SELECT json_build_object(
      'value_count', (SELECT count(*) FROM received_values),
      'outbox_count', (SELECT count(*) FROM outbox_messages),
      'pending', (SELECT count(*) FROM outbox_messages WHERE published_at IS NULL),
      'rows', (SELECT coalesce(json_agg(x), '[]'::json) FROM (
        SELECT o."Id" AS event_id, o.value_id, v.idempotency_key,
          o.published_at IS NOT NULL AS published, o.publish_attempts,
          o.traceparent, o.payload
        FROM outbox_messages o JOIN received_values v ON v."Id" = o.value_id
        WHERE v.idempotency_key LIKE 'runtime-evidence-%'
        ORDER BY o.occurred_at DESC LIMIT 25
      ) x))::text;""",
    "consolidation": """SELECT json_build_object(
      'inbox_count', (SELECT count(*) FROM inbox_messages),
      'count', (SELECT count FROM consolidated_totals WHERE id=1),
      'sum', (SELECT sum FROM consolidated_totals WHERE id=1),
      'rows', (SELECT coalesce(json_agg(x), '[]'::json) FROM (
        SELECT message_id, processed_at FROM inbox_messages
        ORDER BY processed_at DESC LIMIT 25
      ) x))::text;""",
}


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def require_local(url):
    from urllib.parse import urlsplit
    parsed = urlsplit(url)
    if parsed.scheme not in ("http", "https") or parsed.hostname not in (
            "localhost", "127.0.0.1", "::1"):
        raise ValueError("Only local Aspire endpoints are permitted")


def require_disposable():
    if os.getenv("ASPIRE_DISPOSABLE_LAB") != "I_UNDERSTAND":
        raise ValueError("Set ASPIRE_DISPOSABLE_LAB=I_UNDERSTAND only for a disposable local lab")


def psql(database):
    """Credential stays in subprocess environment, never in command/evidence."""
    url = os.getenv("INGESTION_PGURI" if database == "ingestion" else "CONSOLIDATION_PGURI")
    if not url:
        raise ValueError("Set INGESTION_PGURI and CONSOLIDATION_PGURI to the separate application-role URIs")
    expected = "ingestion_db" if database == "ingestion" else "consolidation_db"
    from urllib.parse import urlsplit, unquote, parse_qs
    parsed = urlsplit(url)
    if parsed.scheme not in ("postgres", "postgresql") or parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
        raise ValueError("PostgreSQL must use a localhost URI in a disposable Aspire lab")
    if unquote(parsed.path.lstrip("/")) != expected:
        raise ValueError(f"{database} connection must select {expected}")
    if unquote(parsed.username or "") != ("ingestion_app" if database == "ingestion" else "consolidation_app"):
        raise ValueError("Use the corresponding application database role, not the admin")
    if parsed.query or parsed.fragment:
        raise ValueError("URI query parameters are not permitted in evidence probes")
    # Keep passwords out of argv (process listings) and never copy them to evidence.
    env = dict(os.environ)
    if parsed.password is not None:
        env["PGPASSWORD"] = unquote(parsed.password)
    env["PGHOST"] = parsed.hostname
    env["PGPORT"] = str(parsed.port or 5432)
    env["PGUSER"] = unquote(parsed.username)
    env["PGDATABASE"] = expected
    proc = subprocess.run(["psql", "-X", "-q", "-t", "-A", "-v", "ON_ERROR_STOP=1",
                           "-c", SQL[database]], capture_output=True, text=True, timeout=15,
                          check=False, env=env)
    if proc.returncode:
        raise RuntimeError(f"{database} database probe failed (exit {proc.returncode}; details redacted)")
    return json.loads(proc.stdout.strip())


def safe_db(db):
    """Never write business payloads, traceparent or user idempotency keys to disk."""
    return {k: v for k, v in db.items() if k != "rows"} | {
        "rows": [{k: v for k, v in row.items() if k not in ("payload", "traceparent", "idempotency_key")}
                 for row in db["rows"]]
    }


def capture(ingestion, consolidation):
    http = snapshot(consolidation)
    return {"timestamp_utc": utc(), "http": http,
            "ingestion_db": safe_db(psql("ingestion")),
            "consolidation_db": safe_db(psql("consolidation"))}


def indexed(db, event):
    return next((row for row in db["rows"] if str(row.get("event_id")) == str(event)), None)


def event_for_key(db, key):
    rows = [row for row in db["rows"] if row.get("idempotency_key") == key]
    if len(rows) != 1:
        raise AssertionError("Expected exactly one Outbox entry for the fresh idempotency key")
    return rows[0]


def wait_until(predicate, timeout=90, interval=1):
    deadline = time.monotonic() + timeout
    while True:
        result = predicate()
        if result:
            return result
        if time.monotonic() >= deadline:
            raise AssertionError("Timed out awaiting observed state transition")
        time.sleep(interval)


def observe_event(event_id, expected_count, expected_sum, consolidation, timeout):
    def check():
        state = capture(None, consolidation)
        row = indexed(psql("ingestion"), event_id)
        inbox = any(str(row.get("message_id")) == str(event_id)
                    for row in psql("consolidation")["rows"])
        if (row and row["published"] and inbox
                and state["http"]["count"] == expected_count
                and Decimal(state["http"]["sum"]) == expected_sum):
            return state
        return None
    return wait_until(check, timeout)


def http_post(url, value, key):
    status, receipt = exchange(url, "POST", "/values", str(value), key)
    if status != 201 or not isinstance(receipt, dict) or not receipt.get("id"):
        raise AssertionError(f"POST expected HTTP 201 and receipt ID; observed HTTP {status}")
    return {"status": status, "receipt_id": str(receipt["id"])}


def scenario1(args, record):
    start = capture(args.ingestion, args.consolidation)
    key = "runtime-evidence-" + uuid.uuid4().hex + "-happy"
    post = http_post(args.ingestion, "10.5", key)
    row = wait_until(lambda: next((r for r in psql("ingestion")["rows"] if r["idempotency_key"] == key), None),
                     args.timeout)
    if not row["traceparent"]:
        raise AssertionError("Outbox event lacks persisted W3C traceparent")
    before = start["http"]
    end = observe_event(row["event_id"], before["count"] + 1,
                        Decimal(before["sum"]) + Decimal("10.5"), args.consolidation, args.timeout)
    record["observations"] = {"before": start, "post": post, "event_id": row["event_id"],
                              "outbox_has_traceparent": True, "after": end}
    return row


def scenario2(args, record):
    before = capture(args.ingestion, args.consolidation)
    key = "runtime-evidence-" + uuid.uuid4().hex + "-replay"
    first = http_post(args.ingestion, "5.0", key)
    second_status, second = exchange(args.ingestion, "POST", "/values", "5.00", key)
    conflict_status, _ = exchange(args.ingestion, "POST", "/values", "6", key)
    if (second_status, conflict_status) != (200, 409) or second.get("id") != first["receipt_id"]:
        raise AssertionError("Expected HTTP 201/200/409 and matching receipt IDs")
    row = event_for_key(psql("ingestion"), key)
    after = observe_event(row["event_id"], before["http"]["count"] + 1,
                          Decimal(before["http"]["sum"]) + Decimal("5"), args.consolidation, args.timeout)
    if after["ingestion_db"]["value_count"] != before["ingestion_db"]["value_count"] + 1:
        raise AssertionError("Expected exactly one new persisted value")
    if after["ingestion_db"]["outbox_count"] != before["ingestion_db"]["outbox_count"] + 1:
        raise AssertionError("Expected exactly one new Outbox entry")
    record["observations"] = {"before": before, "statuses": [201, second_status, conflict_status],
                              "receipt_id": first["receipt_id"], "event_id": row["event_id"], "after": after}


def replay(args, row):
    """Management API publish twice with the original payload and MessageId."""
    import base64
    from urllib.parse import quote
    url = os.getenv("RABBITMQ_MANAGEMENT_URL")
    user = os.getenv("RABBITMQ_USERNAME")
    password = os.getenv("RABBITMQ_PASSWORD")
    if not url or not user or not password:
        raise ValueError("Set RABBITMQ_MANAGEMENT_URL, RABBITMQ_USERNAME and RABBITMQ_PASSWORD")
    require_local(url)
    if not url.endswith("/"):
        url += "/"
    endpoint = url + "api/exchanges/%2F/" + quote("lab.events.v1", safe="") + "/publish"
    body = json.dumps({"properties": {"message_id": str(row["event_id"]),
                                      "type": "ValueReceived.v1", "delivery_mode": 2},
                       "routing_key": "value.received.v1", "payload": row["payload"],
                       "payload_encoding": "string"}).encode()
    auth = base64.b64encode(f"{user}:{password}".encode()).decode()
    for _ in range(2):
        req = urllib.request.Request(endpoint, method="POST", data=body,
                                     headers={"Content-Type": "application/json",
                                              "Authorization": "Basic " + auth})
        with urllib.request.urlopen(req, timeout=15) as response:
            answer = json.load(response)
            if response.status != 200 or answer.get("routed") is not True:
                raise AssertionError("RabbitMQ did not confirm that replay reached a queue")


def scenario3(args, record):
    seed = {}
    row = scenario1(args, seed)
    baseline = capture(args.ingestion, args.consolidation)
    replay(args, row)
    time.sleep(3)
    final = capture(args.ingestion, args.consolidation)
    if final["http"] != baseline["http"] or (final["consolidation_db"]["inbox_count"]
                                             != baseline["consolidation_db"]["inbox_count"]):
        raise AssertionError("Duplicate delivery changed the read model or inserted an Inbox row")
    record["observations"] = {"seed": seed["observations"], "amqp_replay_count": 2,
                              "message_id": row["event_id"], "before_replay": baseline,
                              "after_replay": final}
    record["limitations"].append(
        "RabbitMQ routing and HTTP/Inbox invariance observed; verify both deliveries and duplicate "
        "meter/log/span in Aspire before marking full scenario PASS.")


def operator_checkpoint(instruction):
    print("\\nOPERATOR ACTION (local disposable lab): " + instruction, file=sys.stderr)
    input("Press Enter only once completed; Ctrl+C to abort: ")


def pending_event(key):
    return event_for_key(psql("ingestion"), key)


def scenario4(args, record):
    baseline = capture(args.ingestion, args.consolidation)
    operator_checkpoint("STOP only ingestion-outbox-worker in Aspire Resources; leave both APIs up.")
    key = "runtime-evidence-" + uuid.uuid4().hex + "-api-down"
    post = http_post(args.ingestion, "11", key)
    row = pending_event(key)
    if row["published"]:
        raise AssertionError("Outbox was published despite the requested stopped worker checkpoint")
    operator_checkpoint("STOP only ingestion-api; keep consolidation-api running. "
                        "Record the Aspire Resources state and a GET trace before continuing.")
    while_stopped = capture(args.ingestion, args.consolidation)
    if while_stopped["http"] != baseline["http"]:
        raise AssertionError("Independent read changed before resuming the Outbox worker")
    operator_checkpoint("RESTART only ingestion-outbox-worker while ingestion-api remains stopped. "
                        "Do not restart the write API until after final checkpoint.")
    after = observe_event(row["event_id"], baseline["http"]["count"] + 1,
                          Decimal(baseline["http"]["sum"]) + Decimal("11"),
                          args.consolidation, args.timeout)
    record["observations"] = {"before": baseline, "post": post, "pending_event_id": row["event_id"],
                              "while_ingestion_stopped": while_stopped, "after_worker_resumed": after}
    record["limitations"].append("Operator must attach timestamped Aspire resource/GET evidence "
                                 "that ingestion-api remained stopped during publication.")
    operator_checkpoint("RESTART ingestion-api; confirm all resources are healthy and "
                        "record cleanup. Do not omit this recovery step.")


def scenario5(args, record):
    before = capture(args.ingestion, args.consolidation)
    operator_checkpoint("STOP only RabbitMQ in Aspire Resources; keep ingestion-api and PostgreSQL up.")
    key = "runtime-evidence-" + uuid.uuid4().hex + "-broker-down"
    post = http_post(args.ingestion, "7", key)
    row = pending_event(key)
    if row["published"]:
        raise AssertionError("Event was published despite the requested stopped-broker checkpoint")
    during = capture(args.ingestion, args.consolidation)
    if during["http"] != before["http"]:
        raise AssertionError("Read model advanced while broker was expected to be offline")
    operator_checkpoint("RESTART RabbitMQ preserving its local data volume. If required, "
                        "restart the workers. Capture observed failure/recovery logs and metrics.")
    after = observe_event(row["event_id"], before["http"]["count"] + 1,
                          Decimal(before["http"]["sum"]) + Decimal("7"),
                          args.consolidation, args.timeout)
    record["observations"] = {"before": before, "post": post, "pending_event_id": row["event_id"],
                              "during_outage": during, "after_recovery": after}
    record["limitations"].append("Attach actual stopped-broker resource state and worker "
                                 "failure/recovery telemetry; SQL checks do not prove timing.")


def scenario6(args, record):
    before = capture(args.ingestion, args.consolidation)
    if before["http"]["count"] < 1:
        raise AssertionError("Seed at least one event to create consolidated_totals id=1 first")
    # SQL fault injection is operator-controlled, not executed automatically.
    # An interrupted run may leave the injected constraint in place: use the
    # documented disable script before the next run and confirm its removal.

    operator_checkpoint("In dedicated psql session A against DISPOSABLE consolidation_db, "
                        "run scripts/demo/hold-consolidation-lock.sql; leave its transaction open.")
    lock_key = "runtime-evidence-" + uuid.uuid4().hex + "-lock"
    lock_post = http_post(args.ingestion, "13", lock_key)
    lock_row = pending_event(lock_key)
    time.sleep(2)
    while_locked = capture(args.ingestion, args.consolidation)
    if while_locked["http"] != before["http"]:
        raise AssertionError("Aggregate advanced while the row was expected to be locked")
    operator_checkpoint("COMMIT or ROLLBACK psql session A now. Record elapsed time and "
                        "long-running consumer span before proceeding.")
    unlocked = observe_event(lock_row["event_id"], before["http"]["count"] + 1,
                             Decimal(before["http"]["sum"]) + Decimal("13"),
                             args.consolidation, args.timeout)
    operator_checkpoint("Execute scripts/demo/enable-consolidation-failure.sql against "
                        "DISPOSABLE consolidation_db. Keep this constraint installed BRIEFLY.")
    failure_key = "runtime-evidence-" + uuid.uuid4().hex + "-failure"
    failed_post = http_post(args.ingestion, "17", failure_key)
    failure_row = pending_event(failure_key)
    time.sleep(2)
    during_failure = capture(args.ingestion, args.consolidation)
    if during_failure["http"] != unlocked["http"]:
        raise AssertionError("Aggregate changed while failure constraint was expected")
    if any(str(row.get("message_id")) == str(failure_row["event_id"])
           for row in during_failure["consolidation_db"]["rows"]):
        raise AssertionError("Inbox persisted despite failing aggregate transaction")
    operator_checkpoint("REMOVE the injected constraint immediately via "
                        "scripts/demo/disable-consolidation-failure.sql; restart consumer if needed.")
    recovered = observe_event(failure_row["event_id"], unlocked["http"]["count"] + 1,
                              Decimal(unlocked["http"]["sum"]) + Decimal("17"),
                              args.consolidation, args.timeout)
    record["observations"] = {"before": before, "lock_post": lock_post,
                              "lock_event_id": lock_row["event_id"], "while_locked": while_locked,
                              "after_lock_released": unlocked, "failure_post": failed_post,
                              "failure_event_id": failure_row["event_id"],
                              "during_failure": during_failure, "after_failure_recovered": recovered}
    record["limitations"].append("Attach exact SQL injection/rollback timestamps, "
                                 "constraint cleanup proof and live latency/error trace/log/metric evidence.")


def guided(args, record):
    if args.scenario == 4:
        scenario4(args, record)
    elif args.scenario == 5:
        scenario5(args, record)
    else:
        scenario6(args, record)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True, type=int, choices=tuple(SCENARIOS))
    parser.add_argument("--ingestion-url", default=os.getenv("INGESTION_API_URL"))
    parser.add_argument("--consolidation-url", default=os.getenv("CONSOLIDATION_API_URL"))
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--timeout", type=float, default=90)
    args = parser.parse_args(argv)
    try:
        require_disposable()
        if not args.ingestion_url or not args.consolidation_url:
            raise ValueError("Set INGESTION_API_URL and CONSOLIDATION_API_URL from Aspire resources")
        require_local(args.ingestion_url)
        require_local(args.consolidation_url)
        if args.timeout <= 0 or args.output.exists():
            raise ValueError("Timeout must be positive and evidence output must not already exist")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        record = {"scenario": args.scenario, "name": SCENARIOS[args.scenario],
                  "started_utc": utc(), "commit_sha": git_sha(),
                  "result": "INCOMPLETE", "runtime_acceptance": "NOT VERIFIED",
                  "dashboard": {"traces": "NOT INSPECTED", "structured_logs": "NOT INSPECTED",
                                "metrics": "NOT INSPECTED"},
                  "limitations": ["Aspire Dashboard must be inspected manually; no synthetic telemetry IDs."]}
        try:
            if args.scenario == 1:
                scenario1(args, record)
            elif args.scenario == 2:
                scenario2(args, record)
            elif args.scenario == 3:
                scenario3(args, record)
            else:
                guided(args, record)
            record["result"] = "PARTIAL_EVIDENCE"
        except (AssertionError, OSError, ValueError, RuntimeError, KeyError, TypeError,
                EOFError, KeyboardInterrupt, subprocess.TimeoutExpired) as error:
            record["result"] = "FAIL_OR_BLOCKED"
            record["error_type"] = type(error).__name__
            record["error"] = str(error) if isinstance(error, AssertionError) else "Probe failed or was interrupted (details redacted)"
            if args.scenario == 6:
                record["limitations"].append(
                    "MANDATORY CLEANUP: check for an open row-lock transaction and run "
                    "scripts/demo/disable-consolidation-failure.sql against disposable "
                    "consolidation_db before restarting the next scenario.")
        finally:
            record["finished_utc"] = utc()
            # Exclusive creation prevents overwriting prior evidence.
            with args.output.open("x", encoding="utf-8") as target:
                json.dump(record, target, indent=2, ensure_ascii=False)
                target.write("\n")
        print(json.dumps({"result": record["result"], "evidence_file": str(args.output),
                          "runtime_acceptance": record["runtime_acceptance"]}))
        return 0 if record["result"] == "PARTIAL_EVIDENCE" else 1
    except (ValueError, OSError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    sys.exit(main())
