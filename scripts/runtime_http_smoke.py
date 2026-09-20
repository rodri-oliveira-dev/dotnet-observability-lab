#!/usr/bin/env python3
"""Opt-in HTTP smoke evidence for a running, disposable Aspire lab.

This does NOT inspect PostgreSQL, RabbitMQ or the Aspire Dashboard. See
docs/scenarios.md for the manual evidence required by issue #32.
"""
import argparse
import datetime as dt
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
from decimal import Decimal


def exchange(base_url, method, path, value=None, key=None):
    headers = {}
    data = None
    if value is not None:
        data = ('{"value":' + value + '}').encode("utf-8")  # Decimal JSON number, not a JSON string
        headers["Content-Type"] = "application/json"
    if key is not None:
        headers["Idempotency-Key"] = key
    request = urllib.request.Request(base_url.rstrip("/") + path, data=data,
                                     headers=headers, method=method)
    try:
        response = urllib.request.urlopen(request, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        return response.status, json.loads(raw) if raw else {}


def snapshot(base_url):
    status, body = exchange(base_url, "GET", "/consolidated")
    if status != 200 or not isinstance(body, dict):
        raise AssertionError(f"GET /consolidated expected 200, got {status}")
    return {"count": int(body["count"]), "sum": str(Decimal(str(body["sum"])))}


def expect_total(base_url, expected_count, expected_sum, timeout, interval):
    deadline = time.monotonic() + timeout
    last = None
    while True:
        last = snapshot(base_url)
        if (last["count"] == expected_count and
                Decimal(last["sum"]) == expected_sum):
            return last
        if time.monotonic() >= deadline:
            raise AssertionError(
                f"Timed out awaiting count={expected_count}, sum={expected_sum}; "
                f"last observed={last}. Do not infer publication or Inbox success.")
        time.sleep(interval)


def run(ingestion, consolidation, timeout, interval):
    prefix = "runtime-" + uuid.uuid4().hex
    before = snapshot(consolidation)
    count = before["count"]
    total = Decimal(before["sum"])
    observations = {"before": before, "keys": {}}

    happy_key = prefix + "-happy"
    status, receipt = exchange(ingestion, "POST", "/values", "10.5", happy_key)
    if status != 201 or not receipt.get("id"):
        raise AssertionError(f"Happy-path POST expected 201 + id, got {status}")
    observations["keys"]["happy"] = happy_key
    observations["happy"] = {
        "post_status": status, "receipt_id": receipt["id"],
        "after_http": expect_total(consolidation, count + 1, total + Decimal("10.5"),
                                   timeout, interval),
    }

    replay_key = prefix + "-replay"
    results = [
        exchange(ingestion, "POST", "/values", val, replay_key)
        for val in ("5.0", "5.00", "6")
    ]
    statuses = [s for s, _ in results]
    if statuses != [201, 200, 409]:
        raise AssertionError(f"Duplicate-key statuses expected [201,200,409], got {statuses}")
    first_id = results[0][1].get("id")
    if not first_id or results[1][1].get("id") != first_id:
        raise AssertionError("The replay did not return the original receipt id")
    observations["keys"]["replay"] = replay_key
    observations["duplicate_http"] = {
        "statuses": statuses, "first_receipt_id": first_id,
        "replay_receipt_id": results[1][1]["id"],
        "after_http": expect_total(consolidation, count + 2, total + Decimal("15.5"),
                                   timeout, interval),
    }
    return observations


def git_sha():
    result = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True,
                            text=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else "unknown"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ingestion-url", default=os.getenv("INGESTION_API_URL"))
    parser.add_argument("--consolidation-url", default=os.getenv("CONSOLIDATION_API_URL"))
    parser.add_argument("--timeout", type=float, default=90)
    parser.add_argument("--interval", type=float, default=1)
    parser.add_argument("--output", help="Local JSON evidence path (do not commit)")
    args = parser.parse_args(argv)
    if not args.ingestion_url or not args.consolidation_url:
        parser.error("Set both API URLs using flags or the documented environment variables")
    if args.timeout <= 0 or args.interval <= 0:
        parser.error("--timeout and --interval must be positive")

    evidence = {
        "timestamp_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "commit_sha": git_sha(),
        "scope": "HTTP-only smoke; PostgreSQL/AMQP/Traces/Logs/Metrics NOT VERIFIED",
        "happy_http": "NOT RUN", "duplicate_http": "NOT RUN",
        "runtime_scenarios_3_to_6": "NOT RUN",
        "dashboard": "NOT INSPECTED", "database": "NOT INSPECTED",
    }
    exit_code = 1
    try:
        observations = run(args.ingestion_url, args.consolidation_url,
                           args.timeout, args.interval)
        evidence["observations"] = observations
        evidence["happy_http"] = "PASS"
        evidence["duplicate_http"] = "PASS"
        exit_code = 0
    except (AssertionError, OSError, ValueError, KeyError, TypeError) as error:
        # No URLs, passwords, payloads or complete exception trace in the report.
        evidence["error_type"] = type(error).__name__
        evidence["error"] = str(error) if isinstance(error, AssertionError) else "HTTP smoke failed"
    if args.output:
        with open(args.output, "x", encoding="utf-8") as output:
            json.dump(evidence, output, indent=2, ensure_ascii=False)
            output.write("\n")
    print(json.dumps(evidence, indent=2, ensure_ascii=False))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
