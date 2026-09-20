# Live Aspire verification record — issue #32

**Status: NOT VERIFIED.** This is an execution checklist and evidence location, **not**
a claim that any of the six scenarios has passed. A green CI run and the HTTP-only
smoke harness are not evidence of an Aspire Dashboard trace, a RabbitMQ replay,
or PostgreSQL transaction behavior. Keep issue #32 and the runtime checkbox in
[roadmap #13](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/13)
open until every scenario below has actual recorded results.

## Attempt and environment

- Attempt date: 2026-09-20.
- Source reviewed: `main` at `d3c56f238bc2e350f170709d8065ffcb0d391235`.
- Execution environment available for this attempt: GitHub repository access and a
  restricted command environment without `dotnet`, `aspire`, `docker` or network
  access to GitHub. No running AppHost or dashboard was accessible.
- Outcome: **blocked before startup**. No HTTP status, database output, message
  replay, trace, log, or metric has been observed for the six live scenarios.
- Existing GitHub Actions build/tests/coverage and architecture gates are separate
  automated evidence; they must not be substituted for runtime observations.

## Executable HTTP smoke (optional, not a substitute for six scenarios)

In a **disposable, quiet** local lab, start Aspire via [README](../README.md).
Check the four processes and PostgreSQL/RabbitMQ/Redis resources in Aspire;
take the two API URLs from Aspire Resources, not fixed port assumptions.
Install Python 3 and run from the repository root:

```bash
export INGESTION_API_URL='http://localhost:INGESTION_PORT_FROM_ASPIRE'
export CONSOLIDATION_API_URL='http://localhost:CONSOLIDATION_PORT_FROM_ASPIRE'
python3 scripts/runtime_http_smoke.py --output /tmp/lab-http-evidence.json
```

The script makes two unique accepted writes and two requests reusing the second
key. It checks 201 for each first write, 200 for the equivalent duplicate,
409 for conflicting reuse, original receipt identity, and read-model **count/sum
deltas** (not an assumed empty database). It polls the asynchronous read for at
most 90 seconds, exits nonzero on mismatch/timeout, and writes a new local JSON
file with commit SHA, timestamp, statuses, keys, receipt IDs and before/after
HTTP snapshots. **Never commit this locally generated evidence without review.**
The script does *not* connect to either database, publish AMQP messages, stop
resources, inject faults, access the dashboard, or claim those tests passed.
No other writer should modify the read model during the smoke run.

## Live evidence collection (complete all six before checking off #32)

Use [the six scenario commands and safety/cleanup instructions](scenarios.md).
Record the **executed** command or Aspire UI action, date/time (UTC), commit
SHA (`git rev-parse HEAD`), relevant HTTP status and response fields, and
the actual PostgreSQL rows **before and after** each scenario. For the two
databases use their separate non-superuser roles, not the PostgreSQL admin
role. Redact passwords, connection strings, user data and sensitive payloads.

In the Dashboard, capture the resource name, trace ID and the relevant
span names/status/parent relationship; record the structured log timestamp
and correlation ID, and metric name, attributes, and observed before/after
values where available. A blank or missing span is **not PASS**: identify
sampling/export gaps explicitly. The GET is a separate HTTP trace; do not
claim that it is the child of the original POST. Use links to sanitized
screenshots or other retained evidence, never a manually invented trace ID.

For each row below, replace NOT RUN only after executing the scenario; if it
fails, record FAIL, the observed behavior, restoration, the linked fix and
the rerun evidence. Retain the per-scenario evidence alongside this report
or in a linked issue comment; leave private secrets out of the repository.

| Scenario | Execution | HTTP and database evidence | Dashboard traces, structured logs and metrics | Cleanup / rerun |
| --- | --- | --- | --- | --- |
| 1. Accepted write → Outbox → RabbitMQ → Inbox → independent GET | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |
| 2. HTTP duplicate 201/200/409; one value + Outbox | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |
| 3. Re-publish original payload **and AMQP MessageId** twice; no double count | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |
| 4. Stop Ingestion.Api; read unchanged, previously committed Outbox progresses | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |
| 5. Broker down: write/Outbox durable; broker restored and delivery completes | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |
| 6. Lock-induced delay and reversible CHECK-failure; Inbox/aggregate atomic | NOT RUN | NOT RECORDED | NOT INSPECTED | NOT APPLICABLE |

### Minimal per-scenario record to attach

```text
Scenario #:                         Result: NOT RUN / PASS / FAIL / BLOCKED
UTC start/end:                     Commit SHA:
Disposable environment / versions:
Executed commands / UI actions:
Idempotency key / EventId / AMQP MessageId (when applicable):
HTTP codes, receipt IDs, GET before and after:
ingestion_db before / after (value and Outbox, published_at):
consolidation_db before / after (Inbox row count and aggregate):
Trace ID, spans, parent link and separate GET trace:
Structured log event / resource / timestamp:
Metrics: name, tags, before/after observed values:
Sampling/export gaps or unexpected behavior:
Evidence links:
Cleanup performed (restart stopped resources, COMMIT/ROLLBACK lock, remove
  demo_reject_consolidation_updates with checked-in disable script):
Linked bug/PR and actual rerun observations (if failed):
```

**Fault-injection guard:** scenario 6 runs only in disposable
`consolidation_db`. Never enable the failure constraint in real data. Restore
the database immediately after the experiment, including on failure; do not
mark the scenario complete if the lock or constraint remains in place.

## Handoff and acceptance

Issue #33 owns a Portuguese README, its top-of-page language selector and
badges; issue #34 owns rendered/navigable LikeC4 views. Neither was available
in `main` at this attempt, so no bilingual startup or rendered-diagram
navigation was verified. Once the two landing pages exist, link this report
from both with its **pending** status; only describe verified results after
they have been recorded. The GitHub PR for this harness should not use
`Closes #32` while all six runtime verifications remain unexecuted.
