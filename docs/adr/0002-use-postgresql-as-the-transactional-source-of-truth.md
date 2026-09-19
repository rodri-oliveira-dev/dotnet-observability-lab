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

- `ingestion_db` is owned by the non-superuser `ingestion_app` role;
- `consolidation_db` is owned by the non-superuser `consolidation_app` role.

`Ingestion.Api` and `Ingestion.Outbox.Worker` receive a connection string containing only the `ingestion_app` credentials and `ingestion_db`.

`Consolidation.Api` and `Consolidation.Worker` receive a connection string containing only the `consolidation_app` credentials and `consolidation_db`.

The PostgreSQL administrator role is reserved for local provisioning and health operations and is not injected into application processes.

`PUBLIC CONNECT` is revoked from both application databases, and each application role receives `CONNECT` only to its owned database. The database owner governs its local `public` schema through PostgreSQL's `pg_database_owner` behavior, so EF migrations do not require superuser credentials.

Each boundary owns its EF Core `DbContext`, mappings, migrations, tables, constraints, and transaction boundaries. No application may query or write the other boundary's database.

PostgreSQL is the durable source of truth for correctness. Later idempotency, Outbox, and Inbox implementations must use PostgreSQL transactions and constraints where correctness depends on atomicity or uniqueness.

The local PostgreSQL resource uses persistent container storage. Its administrator password and both application-role passwords are secret parameters stored outside source control and must remain stable for the lifetime of the data volume.

## Consequences

Positive consequences:

- transaction and unique-constraint semantics can provide durable correctness;
- data ownership is enforced by PostgreSQL credentials and database privileges, not only by application wiring;
- compromising one application role does not grant access to the other boundary's database;
- the PostgreSQL administrator credential is not exposed to application processes;
- one local PostgreSQL container keeps the lab lightweight;
- EF Core migrations remain owned by the boundary whose schema they change.

Trade-offs and constraints:

- physical server failure affects both logical databases in the local lab;
- the local topology is not a statement that production deployment must use one PostgreSQL server;
- a fresh PostgreSQL volume is required when introducing the init roles to an already-created disposable development volume;
- changing a persisted database password requires altering the corresponding PostgreSQL role or recreating a disposable local volume;
- schema changes require new migrations and must never be implemented through cross-database queries.
