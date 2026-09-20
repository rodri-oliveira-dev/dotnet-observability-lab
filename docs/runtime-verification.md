# Live Aspire verification record — follow-up issue #40

**Status: NOT VERIFIED.** This is an execution checklist and evidence location, **not**
a claim that any of the six scenarios has passed. A green CI run and the HTTP-only
smoke harness are not evidence of an Aspire Dashboard trace, a RabbitMQ replay,
or PostgreSQL transaction behavior. [Issue #32](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/32)
was closed **administratively without runtime acceptance**; [follow-up issue #40](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/40)
tracks the still-unexecuted six-scenario verification. [Roadmap #13](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/13)
may be closed for delivered implementation/documentation, but this runtime verification
must remain explicitly **pending** until actual evidence exists.

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

## Guided live evidence collector (issue #40)

The optional [live evidence runner](../scripts/runtime_evidence.py) probes **real local
Aspire HTTP endpoints, separate application-role PostgreSQL databases and,
for scenario 3, publishes the original stored payload and AMQP MessageId twice
through the local RabbitMQ Management API**. Scenarios 4–6 pause at operator
checkpoints to capture the before/during/after state while you stop/restart
resources or run the documented disposable-database SQL fault scripts. No
runtime disruptions or SQL fault injection happen automatically.

The runner does **not** start Aspire, authenticate to the Aspire Dashboard,
export OpenTelemetry signals, or declare the scenarios fully verified.
Its output always says `runtime_acceptance: NOT VERIFIED` and its result
`PARTIAL_EVIDENCE` means only the implemented HTTP/database/AMQP assertions
passed. Trace IDs, real correlated structured logs, metric measurements,
precise operator-action timestamps and cleanup evidence must be captured
from the **running Aspire Dashboard**, attached to each scenario and reviewed
before the issue's checklist or this report is marked PASS.

Use a **disposable lab with no competing writers**. Install `psql` and Python
3.10+ on the host running the local AppHost. From the Aspire Resources page,
copy the *current* API HTTP addresses, PostgreSQL external host/port and
RabbitMQ Management HTTP port. Use the same five configured persistent
secrets from your local secret store; never save them in a committed file.
Keep the database URI pointing to `localhost`, with the boundary-specific
non-superuser and database (the runner rejects remote hosts, administrators
and cross-boundary database names). For example, in a private shell:

```bash
export ASPIRE_DISPOSABLE_LAB=I_UNDERSTAND
export INGESTION_API_URL='http://localhost:INGESTION_HTTP_PORT'
export CONSOLIDATION_API_URL='http://localhost:CONSOLIDATION_HTTP_PORT'
export INGESTION_PGURI='postgresql://ingestion_app:URL_ENCODED_PASSWORD@localhost:POSTGRES_PORT/ingestion_db'
export CONSOLIDATION_PGURI='postgresql://consolidation_app:URL_ENCODED_PASSWORD@localhost:POSTGRES_PORT/consolidation_db'
export RABBITMQ_MANAGEMENT_URL='http://localhost:RABBITMQ_MANAGEMENT_PORT'
export RABBITMQ_USERNAME='YOUR_LOCAL_RABBITMQ_USERNAME'
export RABBITMQ_PASSWORD='YOUR_LOCAL_RABBITMQ_PASSWORD'
python3 scripts/runtime_evidence.py --scenario 1 --output /tmp/aspire-scenario-1.json
python3 scripts/runtime_evidence.py --scenario 2 --output /tmp/aspire-scenario-2.json
python3 scripts/runtime_evidence.py --scenario 3 --output /tmp/aspire-scenario-3.json
python3 scripts/runtime_evidence.py --scenario 4 --output /tmp/aspire-scenario-4.json
python3 scripts/runtime_evidence.py --scenario 5 --output /tmp/aspire-scenario-5.json
python3 scripts/runtime_evidence.py --scenario 6 --output /tmp/aspire-scenario-6.json
```

Each command is **opt-in and independent**; do not run the six at once.
Run scenario 1 first to create the aggregate, then 2–3 before 4–6. Scenario
3 needs RabbitMQ Management enabled and routes two copies of the persisted
event with the **original** AMQP `message_id`; check the actual consumer
duplicate meter/log/span separately before accepting the scenario.
Scenario 4 stops only the Outbox worker before the new POST; scenario 5
requires the broker to be stopped **before** the new POST. Scenario 6 is
interactive: create and release the row lock in a dedicated `psql` session,
then install the failure constraint **briefly** and always remove it with
[disable-consolidation-failure.sql](../scripts/demo/disable-consolidation-failure.sql)
even if the runner errors or is interrupted. Verify no lock/constraint
remains, restart any stopped resources and record their observed health.

Evidence files contain real but *partial* observations (counts/sums, receipt
and message IDs, timestamps and publication state). They intentionally omit
database payloads, W3C traceparent and idempotency keys, and redact probe
errors, but should still be treated as sensitive local output. Inspect and
redact before sharing. The script uses exclusive file creation and refuses
to overwrite an earlier run. Never upload credentials, raw `PGURI`, RabbitMQ
authentication headers or unreviewed screenshots.

**Acceptance:** for each scenario attach the redacted JSON *and* a separate
manual record using the template below with real Dashboard trace/log/metric
IDs and observed values, action timestamps, and cleanup evidence. A JSON
`PARTIAL_EVIDENCE` result is not PASS and must not close issue #40.

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

The English and Brazilian Portuguese READMEs (merged via [PR #38](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/pull/38))
and the GitHub-rendered LikeC4 gallery (merged via [PR #39](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/pull/39))
are now present in `main`. **Neither was available at the recorded attempt above**;
their presence does not retroactively establish a live startup or Dashboard verification.
Both READMEs link to this report with its **pending** status.

Use [follow-up #40](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/40)
for execution results and evidence links. Only change `NOT VERIFIED` after all six
scenarios have actual, redacted observations. The administrative closures of
[original issue #32](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/32)
and [implementation/documentation roadmap #13](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/13)
do **not** signify runtime acceptance.
