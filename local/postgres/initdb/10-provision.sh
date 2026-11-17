#!/usr/bin/env bash
# ISK-0008 developer topology: run once by the postgres image on an empty data volume, after initdb (local-environment
# contract, PostgreSQL rules 1 and 2; research R7). It creates the three ISK-0004 identities and nothing else: no
# schema, table, or row, and no migration. Passwords reach psql from its environment through \getenv, never on a
# command line or in SQL text, and a failed statement is never echoed.
set -euo pipefail

if [ -z "${ISKENDER_LOCAL_RUNTIME_PASSWORD:-}" ] || [ -z "${ISKENDER_LOCAL_MIGRATION_PASSWORD:-}" ]; then
  echo "10-provision.sh: a password variable is empty; run local/scripts/reset.sh, then local/scripts/start.sh" >&2
  exit 1
fi

psql -v ON_ERROR_STOP=1 --no-psqlrc --quiet --username "${POSTGRES_USER:-postgres}" --dbname "$POSTGRES_DB" <<'SQL'
\set VERBOSITY terse
SET log_min_error_statement = panic;
\getenv runtime_password ISKENDER_LOCAL_RUNTIME_PASSWORD
\getenv migration_password ISKENDER_LOCAL_MIGRATION_PASSWORD
CREATE ROLE iskender_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
CREATE ROLE iskender_local_runtime LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE IN ROLE iskender_runtime PASSWORD :'runtime_password';
CREATE ROLE iskender_local_migrator LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD :'migration_password';
GRANT CONNECT ON DATABASE iskender_local TO iskender_local_runtime, iskender_local_migrator;
GRANT CREATE ON DATABASE iskender_local TO iskender_local_migrator;
SQL
