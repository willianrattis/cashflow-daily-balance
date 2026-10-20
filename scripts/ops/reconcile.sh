#!/usr/bin/env bash
# Reconciles LaunchService's ledger (launch_db.launches) against ConsolidationService's
# projection (consolidation_db.daily_consolidations), per business_date.
#
# This is OPERATIONAL TOOLING, run by hand or from a runbook/cron against the Docker Compose
# stack — it is NOT service-to-service coupling. Neither service calls the other to produce
# this result: this script reads both databases directly via `docker compose exec psql`,
# outside the request path, and compares the two sums itself. It proves (or disproves) that
# eventual consistency has actually converged; it does not enforce or participate in it.
#
# Uses the admin/superuser role (`cashflow`) on purpose — it is the only role allowed to read
# both databases from one script; the services themselves connect as the non-superuser
# `launch_svc`/`consolidation_svc` roles, each unable to reach the other's database
# (docs/05-aspectos-transversais.md §1).
#
# Usage:
#   scripts/ops/reconcile.sh
#
# Exit code: 0 if the ledger and the projection agree on every business_date;
# 1 if at least one mismatch is found (printed to stdout, "<" = ledger, ">" = projeção).
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

LEDGER_SQL="SELECT business_date || '|' ||
    COALESCE(SUM(amount_in_cents) FILTER (WHERE nature = 'Credit'), 0) || '|' ||
    COALESCE(SUM(amount_in_cents) FILTER (WHERE nature = 'Debit'), 0)
  FROM launches
  GROUP BY business_date
  ORDER BY business_date;"

PROJECTION_SQL="SELECT business_date || '|' || credits_in_cents || '|' || debits_in_cents
  FROM daily_consolidations
  ORDER BY business_date;"

echo "Lendo launch_db.launches..."
ledger=$(docker compose exec -T postgres psql -U cashflow -d launch_db -t -A -c "$LEDGER_SQL")

echo "Lendo consolidation_db.daily_consolidations..."
projection=$(docker compose exec -T postgres psql -U cashflow -d consolidation_db -t -A -c "$PROJECTION_SQL")

echo
echo "Formato de cada linha: business_date|credits_in_cents|debits_in_cents"
if diff_output=$(diff <(echo "$ledger") <(echo "$projection")); then
  echo "RECONCILE: PASS — ledger (launch_db) e projeção (consolidation_db) conferem para todas as business_date."
  exit 0
else
  echo "RECONCILE: FAIL — divergência entre ledger e projeção ('<' = launch_db, '>' = consolidation_db):"
  echo "$diff_output"
  exit 1
fi
