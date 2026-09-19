#!/usr/bin/env bash
set -euo pipefail

psql   --set=ON_ERROR_STOP=1   --username "$POSTGRES_USER"   --dbname "$POSTGRES_DB"   --set=ingestion_password="$INGESTION_DB_PASSWORD"   --set=consolidation_password="$CONSOLIDATION_DB_PASSWORD" <<'EOSQL'
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;

CREATE ROLE ingestion_app
  LOGIN
  NOSUPERUSER
  NOCREATEDB
  NOCREATEROLE
  NOREPLICATION
  NOBYPASSRLS;
ALTER ROLE ingestion_app PASSWORD :'ingestion_password';

CREATE ROLE consolidation_app
  LOGIN
  NOSUPERUSER
  NOCREATEDB
  NOCREATEROLE
  NOREPLICATION
  NOBYPASSRLS;
ALTER ROLE consolidation_app PASSWORD :'consolidation_password';
EOSQL
