# Use PostgreSQL as the transactional source of truth

## Status

Accepted

## Context

The lab needs durable correctness for two independent service boundaries:

- ingestion must eventually persist received values, HTTP idempotency state, and Transactional Outbox records;
- consolidation must eventually persist Inbox records and the consolidated read model.

Those operations require transactional atomicity and database-enforced uniqueness. The local environment should remain small and easy to run, but the read and write boundaries must not share tables or query each other's data.

Running a separate PostgreSQL server container for each boundary would provide stronger physical isolation but would add local operational cost without improving the learning objective of this lab.

Using a single shared logical database would reduce local resources but would make cross-boundary access easy and obscure data ownership.

## Decision

Use **one PostgreSQL server resource** in the local Aspire environment and create two separately owned logical databases:

- `ingestion_db` belongs exclusively to the ingestion boundary;
- `consolidation_db` belongs exclusively to the consolidation boundary.

`Ingestion.Api` and `Ingestion.Outbox.Worker` receive only the `ingestion_db` connection.

`Consolidation.Api` and `Consolidation.Worker` receive only the `consolidation_db` connection.

Each boundary owns its EF Core `DbContext`, mappings, migrations, tables, constraints, and transaction boundaries. No application may query or write the other boundary's database.

PostgreSQL is the durable source of truth for correctness. Later idempotency, Outbox, and Inbox implementations must use PostgreSQL transactions and constraints where correctness depends on atomicity or uniqueness.

The local PostgreSQL resource uses persistent container storage to preserve database state across normal development restarts.

## Consequences

Positive consequences:

- transaction and unique-constraint semantics can provide durable correctness;
- data ownership is explicit even though the local environment uses one PostgreSQL server;
- the consolidation side can remain independent from the ingestion side;
- one local PostgreSQL container keeps the lab lightweight;
- EF Core migrations remain owned by the boundary whose schema they change.

Trade-offs and constraints:

- physical server failure affects both logical databases in the local lab;
- the local topology is not a statement that production deployment must use one PostgreSQL server;
- ownership must be enforced through project wiring, reviews, and later architecture tests because both databases share one local server process;
- schema changes require new migrations and must never be implemented through cross-database queries.
