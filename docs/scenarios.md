# Reproducible resilience and observability scenarios

This runbook exercises the **implemented** four-process lab against a disposable Aspire development environment; never run the fault-injection SQL or event replays against production data. The [static C4 views and dynamic happy-path view](architecture/README.md) describe responsibilities and the intended order; the **Traces** tab in Aspire shows sampled *actual executions* (including asynchronous retries and failures), not a rendering of the LikeC4 diagram. A subsequent GET starts a separate trace. Aspire receives OTLP telemetry but carries no business traffic.

## Preparation (once per fresh clone)

Follow [the root quick-start](../README.md) to configure five secrets and run the AppHost. Docker/OCI runtime must be running; the two HTTP URLs come from Aspire's Resources page, **not fixed ports**. In a new shell, substitute those resource URLs and export:

```bash
export INGESTION_API_URL='http://localhost:PORT_FROM_ASPIRE'
export CONSOLIDATION_API_URL='http://localhost:OTHER_PORT_FROM_ASPIRE'
export DEMO_KEY="lab-$(date +%s)-$$"
```

Use curl, a SQL client connected as `ingestion_app` to `ingestion_db` and as `consolidation_app` to `consolidation_db`, and the RabbitMQ management UI linked from the Aspire `rabbitmq` resource. The application-role passwords were configured at startup; never use one role to query the other database. For a local `psql` client, e.g. `PGPASSWORD="$INGESTION_PASSWORD" psql -h HOST_FROM_ASPIRE -p PORT_FROM_ASPIRE -U ingestion_app -d ingestion_db`, obtaining the PostgreSQL host/port from Aspire's resource endpoints; repeat with `consolidation_app` for its database. These URLs and ports vary with the local runtime. Run the following inspections **against their respective databases**:

```sql
-- ingestion_db only
SELECT "Id", value_id, payload, traceparent, published_at, quarantined_at,
       publish_attempts, next_attempt_at
FROM outbox_messages ORDER BY occurred_at DESC LIMIT 10;

-- consolidation_db only
SELECT id, count, sum, last_updated_at FROM consolidated_totals WHERE id = 1;
SELECT message_id, processed_at FROM inbox_messages ORDER BY processed_at DESC LIMIT 10;
```

The output uses application database column names (`outbox_messages."Id"` versus lowercase `inbox_messages.message_id`). Use fresh `DEMO_KEY` values for separate first-time writes. An existing database may already have totals; compare **deltas**, not an assumed global zero baseline. The Outbox retry interval is bounded and an event may take several seconds to reach consolidation.

## 1. Happy path: durable write → asynchronous read model

```bash
curl -i -X POST "$INGESTION_API_URL/values" \
  -H "Idempotency-Key: $DEMO_KEY-happy" -H 'Content-Type: application/json' \
  -d '{"value":10.5}'
curl -i "$CONSOLIDATION_API_URL/consolidated"
```

Expect 201 for the POST. After publication/consumption, the matching Outbox row has `published_at` and the Inbox contains its `"Id"` as `message_id`; the read-model `count` increases by **1** and `sum` by **10.5**. If the immediate GET is stale, repeat after a short delay: processing is asynchronous. In Aspire **Traces**, select `ingestion-api` / the POST, then expand `ingestion.accept_value`, `rabbitmq publish ValueReceived.v1` (`ingestion-outbox-worker`), `rabbitmq process ValueReceived.v1` and `consolidation.process_value` (`consolidation-worker`). The Outbox stores the original `traceparent` and the publisher carries it forward in AMQP headers. The GET has its **own** HTTP trace. In **Structured Logs**, inspect the selected resources' committed publication/consumption logs correlated with the trace; in **Metrics**, inspect `lab.ingestion.values.accepted`, `lab.outbox.messages.published`, `lab.consolidation.messages.consumed`, `lab.consolidation.values.processed` and `lab.consolidation.processing.duration`. Sampling/export delays can omit spans; the database and HTTP response establish correctness.

## 2. Duplicate HTTP request: same logical value, one Outbox row

```bash
curl -i -X POST "$INGESTION_API_URL/values" -H "Idempotency-Key: $DEMO_KEY-replay" -H 'Content-Type: application/json' -d '{"value":5.0}'
curl -i -X POST "$INGESTION_API_URL/values" -H "Idempotency-Key: $DEMO_KEY-replay" -H 'Content-Type: application/json' -d '{"value":5.00}'
curl -i -X POST "$INGESTION_API_URL/values" -H "Idempotency-Key: $DEMO_KEY-replay" -H 'Content-Type: application/json' -d '{"value":6}'
```

Expect **201, 200, 409**: the first two receipts have identical `id`, and the last is a conflict. Query `ingestion_db`: exactly one `received_values` row for this key and **one** linked Outbox row. Only the first POST increments `lab.ingestion.values.accepted`; duplicate attempts increment `lab.ingestion.requests.duplicate` with bounded `result=replayed` and `result=conflict` tags. Inspect the `ingestion.accept_value` span's `idempotency.status` and `IdempotencyDuplicate` structured log. Redis is a best-effort shortcut, not the uniqueness guarantee.

```sql
-- ingestion_db only; psql variable substitution is optional here
SELECT v.id, v.idempotency_key, COUNT(o."Id") AS outbox_events
FROM received_values v LEFT JOIN outbox_messages o ON o.value_id = v.id
WHERE v.idempotency_key LIKE 'lab-%-replay'
GROUP BY v.id, v.idempotency_key ORDER BY v.idempotency_key DESC LIMIT 10;
```

## 3. Duplicate AMQP delivery: Inbox prevents double counting

First run scenario 1 and save the **actual** `payload` and `"Id"` from its Outbox row. In the RabbitMQ **Management** UI open Exchanges → `lab.events.v1` → Publish message. Use routing key `value.received.v1`, the saved JSON payload as the body, and AMQP **properties** `message_id` equal to the saved `"Id"` (GUID) and `type` = `ValueReceived.v1`. If the UI version cannot edit AMQP properties, use an AMQP client that supports them; merely pasting the JSON without the original `MessageId` is **not** a valid replay for this consumer. Publish the exact same message **twice**, without changing EventId, ValueId or MessageId. The worker may receive it twice, but `consolidated_totals.count/sum` and the number of Inbox rows remain unchanged. In Aspire, `lab.consolidation.messages.consumed` and `lab.consolidation.messages.duplicate` increase, but `lab.consolidation.values.processed` does not. Inspect the consumer/process spans (`consolidation.result=duplicate`) and `Duplicate` structured log. A replay without trace headers may appear in a new trace even though its business identity is unchanged.

## 4. Ingestion.Api unavailable: independent durable read and Outbox worker

Complete scenario 1 and save the GET response. In Aspire Resources, stop **only** `ingestion-api`, leaving `consolidation-api`, PostgreSQL, RabbitMQ and both workers running. Repeat `curl -i "$CONSOLIDATION_API_URL/consolidated"`: HTTP **200** serves the same last committed snapshot; there is no synchronous API-to-API call. To demonstrate **previously committed** Outbox work continuing, stop `ingestion-outbox-worker` before creating a **new** value, POST it successfully, then stop `ingestion-api` and restart **only** `ingestion-outbox-worker`. Inspect the original pending Outbox row turning `published_at` non-null and the consolidated snapshot advancing while the write API remains stopped. Do not try to create a new HTTP write after stopping ingestion. The GET creates an independent read trace in Aspire.

## 5. RabbitMQ temporarily unavailable: no accepted write lost

Ensure the worker is running, then stop **only** the `rabbitmq` resource in Aspire. Keep PostgreSQL and `ingestion-api` running; POST a fresh key:

```bash
curl -i -X POST "$INGESTION_API_URL/values" -H "Idempotency-Key: $DEMO_KEY-broker-down" -H 'Content-Type: application/json' -d '{"value":7}'
```

Expect **201** even though the broker is offline: the value and Outbox row commit in `ingestion_db`. The row has `published_at IS NULL`; the independently running worker eventually records `publish_attempts` / `next_attempt_at` and `OutboxPublishFailed`. Aspire Metrics: `lab.outbox.messages.pending` includes delayed retries, and `lab.outbox.messages.publish_failures` counts failed attempts; these signals are best-effort and the gauge's last sample can be stale if the worker is down. Restart `rabbitmq` **with its existing local data volume**; verify the worker reconnects (restart the worker if it exited during broker initialization), the row becomes published after broker confirmation, and the consumer eventually commits its Inbox/read model. If a broker confirm succeeded but the Outbox database commit failed, a retry can deliver a duplicate; scenario 3 shows why the Inbox is durable.

## 6. Slow/error consolidation: reversible development-only database faults

Only on a **disposable local** `consolidation_db`. No production feature flags or changes to normal application code are used. First finish scenario 1 so singleton `consolidated_totals.id=1` exists. All commands below run as `consolidation_app` against `consolidation_db`; keep RabbitMQ and both workers running. Avoid competing demo runs.

**Slow processing:** open a dedicated SQL session A and run the checked-in [lock script](../scripts/demo/hold-consolidation-lock.sql). Leave session A open **inside its transaction**. POST a *new* key/value using the scenario 1 curl template. The consumer can insert its Inbox row but blocks on the locked aggregate row (the entire transaction remains uncommitted, so there is no durable half-write). In Aspire Traces, observe the long `consolidation.process_value` span, and in Metrics the elevated `lab.consolidation.processing.duration` *after it finishes*. Check Structured Logs for the eventual committed event. Return to session A and execute `COMMIT;` to release the lock; verify that the GET advances once, not twice. Do not leave the transaction open after the demo.

**Error processing:** run [enable-failure.sql](../scripts/demo/enable-consolidation-failure.sql) once on the same disposable database, then POST a new key/value. The injected CHECK constraint makes the worker's aggregate UPSERT fail; the whole Inbox/aggregate transaction rolls back and the broker delivery is nacked/requeued. Inspect the consumer span's **error** status, `Consolidation failed; delivery requeued` in Structured Logs, and `lab.consolidation.processing.duration{result=failed}` in Metrics. `GET /consolidated` still serves the **previous** persisted snapshot. **Always restore immediately:** run [disable-failure.sql](../scripts/demo/disable-consolidation-failure.sql), then allow/restart the consumer to process the queued event. Check that the read model advances once and the Inbox contains just one row for this MessageId. The repeated nacks while the constraint is active can create a tight retry loop; keep the injected failure brief and stop `consolidation-worker` if necessary to perform cleanup. These scripts must never be run against real data.

## Expected signal glossary

| Signal | Where to find it / interpretation |
| --- | --- |
| `lab.ingestion.values.accepted` | Ingestion API meter; increases once per new committed value. |
| `lab.ingestion.requests.duplicate` | Ingestion API meter, bounded `result=replayed\|conflict` tags. |
| `lab.outbox.messages.pending` | Outbox worker gauge of **all** unpublished, unquarantined rows, including delayed retries; do not sum snapshots across workers. |
| `lab.outbox.messages.publish_failures` / `lab.outbox.messages.published` | Outbox worker; retry attempts versus confirmed+committed publications. |
| `lab.consolidation.messages.consumed` / `lab.consolidation.messages.duplicate` / `lab.consolidation.values.processed` | Consumer deliveries, Inbox duplicate decisions, successful unique consolidations respectively. |
| `lab.consolidation.processing.duration` | Worker histogram, seconds, bounded `result=applied\|duplicate\|failed` tags. |

A trace may be absent due to sampling/export, and HTTP/DB state is authoritative. Never use a metric counter as a proof of exactly-once delivery; the implementation guarantees idempotent *business effect*, not exactly-once transport.
