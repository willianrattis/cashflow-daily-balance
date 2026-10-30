#!/usr/bin/env bash
# Runs once, on first container init, via /docker-entrypoint-initdb.d (official postgres image) —
# only on an EMPTY data volume. If you change anything below, `docker compose down -v` (drop the
# `postgres-data` volume) before `up` again, or it silently keeps running against the old state.
#
# POSTGRES_DB (cashflow_entry_db) is created automatically by the base image; this script adds the second
# logical database so one PostgreSQL server can host both, while CashFlowEntryService and
# ConsolidationService still never share tables (ADR-001, ADR-006) — and, below, creates the two
# non-superuser roles each service actually connects with, so neither one runs as the Postgres
# superuser (docs/05-aspectos-transversais.md §1, ADR-006). `$POSTGRES_USER` (the superuser) stays
# the admin role used by `scripts/ops/*` and `scripts/postgres/*`, which legitimately need to read
# or write both databases from one script.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "postgres" <<-SQL
    SELECT 'CREATE DATABASE consolidation_db OWNER ${POSTGRES_USER}'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'consolidation_db')\gexec
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "postgres" <<-SQL
    SELECT format('CREATE ROLE cashflow_entry_svc LOGIN PASSWORD %L', '${CASHFLOW_ENTRY_SVC_PASSWORD}')
    WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'cashflow_entry_svc')\gexec

    SELECT format('CREATE ROLE consolidation_svc LOGIN PASSWORD %L', '${CONSOLIDATION_SVC_PASSWORD}')
    WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'consolidation_svc')\gexec

    ALTER DATABASE cashflow_entry_db OWNER TO cashflow_entry_svc;
    ALTER DATABASE consolidation_db OWNER TO consolidation_svc;

    -- Least privilege: the default grant of CONNECT to PUBLIC would let either service (or
    -- anything else authenticating to this server) reach the other's database even without
    -- knowing its role's password is relevant — REVOKE closes that, then each service's own role
    -- gets CONNECT back only to its own database.
    REVOKE CONNECT ON DATABASE cashflow_entry_db FROM PUBLIC;
    REVOKE CONNECT ON DATABASE consolidation_db FROM PUBLIC;
    GRANT CONNECT ON DATABASE cashflow_entry_db TO cashflow_entry_svc;
    GRANT CONNECT ON DATABASE consolidation_db TO consolidation_svc;
SQL

# The `public` schema inside each database still needs to belong to that database's own role so
# EF Core migrations (CREATE TABLE, trigger, FK, ...) run as that role succeed without further
# grants.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "cashflow_entry_db" -c "ALTER SCHEMA public OWNER TO cashflow_entry_svc;"
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "consolidation_db" -c "ALTER SCHEMA public OWNER TO consolidation_svc;"
