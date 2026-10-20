#!/usr/bin/env bash
# Seeds `consolidation_db.daily_consolidations`/`projection_metadata` directly with N days of
# history, so tests/load/consolidation-50rps.js exercises a realistic query window (>= 90 days)
# instead of an empty/all-zero projection.
#
# Deliberately a seed script, not the public API: `POST /launches` always assigns `businessDate`
# as today (docs/03-contratos.md — no backdating), so there is no way to produce 90 days of
# *history* through the API alone. Writes directly to Postgres as the admin role (`cashflow`) on
# purpose, like every script under scripts/postgres/ and scripts/ops/
# (docs/05-aspectos-transversais.md §1) — this is operational/test tooling, not the service itself.
#
# Usage:
#   scripts/load/seed-consolidation-history.sh [days]   # days default 90
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

DAYS="${1:-90}"

docker compose exec -T postgres psql -U cashflow -d consolidation_db -v ON_ERROR_STOP=1 -q \
  -v days="$DAYS" <<'SQL'
INSERT INTO daily_consolidations
    (business_date, credits_in_cents, debits_in_cents, net_change_in_cents,
     last_event_occurred_at_utc, projection_version, updated_at_utc)
SELECT
    (CURRENT_DATE - gs)::date,
    (1500 + (gs * 37) % 5000)::bigint AS credits,
    (700 + (gs * 23) % 3000)::bigint AS debits,
    ((1500 + (gs * 37) % 5000) - (700 + (gs * 23) % 3000))::bigint AS net_change,
    now(),
    1,
    now()
FROM generate_series(1, :days) AS gs
ON CONFLICT (business_date) DO UPDATE SET
    credits_in_cents = EXCLUDED.credits_in_cents,
    debits_in_cents = EXCLUDED.debits_in_cents,
    net_change_in_cents = EXCLUDED.net_change_in_cents,
    last_event_occurred_at_utc = EXCLUDED.last_event_occurred_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;

INSERT INTO projection_metadata
    (id, last_applied_event_recorded_at_utc, last_applied_at_utc, projection_version)
VALUES
    (1, now(), now(), :days)
ON CONFLICT (id) DO UPDATE SET
    last_applied_event_recorded_at_utc = EXCLUDED.last_applied_event_recorded_at_utc,
    last_applied_at_utc = EXCLUDED.last_applied_at_utc,
    projection_version = GREATEST(projection_metadata.projection_version, EXCLUDED.projection_version);
SQL

echo "Seeded $DAYS days of daily_consolidations history."
