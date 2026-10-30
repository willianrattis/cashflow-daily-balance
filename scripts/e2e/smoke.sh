#!/usr/bin/env bash
# End-to-end smoke harness against the Docker Compose stack (MR-06 / NFR-A).
#
# Automates the proof that CashFlowEntryService keeps accepting writes while
# ConsolidationService or RabbitMQ are down (docs/10-roteiro-de-testes.md §7 and §8),
# plus idempotency and payload-mismatch checks already documented in docs/03-contratos.md.
#
# Usage: scripts/e2e/smoke.sh [--durability] [--recovery]
#   --durability   also run the opt-in broker-restart-survives-outage check:
#                   stops ConsolidationService, registers a credit, brings the whole
#                   compose stack down (no -v) and back up, then confirms the credit
#                   still reaches the consolidation. Disruptive to the whole stack, so
#                   it is not part of the default run.
#   --recovery     also run the opt-in recovery tooling check (FR-007):
#                   scripts/ops/reconcile.sh (expect PASS), scripts/ops/rebuild-consolidation.sh
#                   --yes (full projection rebuild from the Outbox), scripts/ops/reconcile.sh
#                   again (expect PASS). Disruptive (stops/restarts consolidationservice,
#                   truncates the projection tables), so it is not part of the default run.
#
# Dependencies: curl, python3 (standard library only), docker compose. No jq, no uuidgen.
#
# Consolidation assertions are relative to a baseline captured at the start of the run, not
# absolute totals: the projection has a single global row per business date, so a second run
# against data left over from a previous one (no `docker compose down -v` in between) must not
# expect to start from zero.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

RUN_DURABILITY=false
RUN_RECOVERY=false
for arg in "$@"; do
  case "$arg" in
    --durability) RUN_DURABILITY=true ;;
    --recovery) RUN_RECOVERY=true ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

ENTRY_BASE="${ENTRY_BASE:-http://localhost:5101}"
CONSOLIDATION_BASE="${CONSOLIDATION_BASE:-http://localhost:5102}"
RUN_ID="smoke-$(date +%s)"
TODAY="$(TZ="America/Sao_Paulo" date +%Y-%m-%d)"

# How long to keep a dependency down after the POST, before restarting it, so the
# publisher/consumer retry path is actually exercised instead of a near-instant restart.
OUTAGE_SECONDS="${OUTAGE_SECONDS:-15}"

CONSOLIDATION_QUEUE_NAME="consolidation.cashflow-entry-recorded.v1"
DLQ_QUEUE_NAME="consolidation.cashflow-entry-recorded.v1.dlq"
RABBITMQ_MGMT_BASE="${RABBITMQ_MGMT_BASE:-http://localhost:15672}"
RABBITMQ_MGMT_AUTH="${RABBITMQ_MGMT_AUTH:-cashflow:cashflow-dev-only}"

TMP_DIR="$(mktemp -d)"
BODY_FILE="$TMP_DIR/body.json"
HEADERS_FILE="$TMP_DIR/headers.txt"

CONSOLIDATION_STOPPED=false
RABBITMQ_STOPPED=false
DURABILITY_DOWN=false

STEP_NAMES=()
STEP_STATUS=()
STEP_ELAPSED=()
OVERALL_FAIL=0
LAST_STEP_STATUS=""

cleanup() {
  local rc=$?
  if [ "$DURABILITY_DOWN" = "true" ]; then
    echo "cleanup: bringing the compose stack back up (was left down by --durability)" >&2
    docker compose up -d >/dev/null 2>&1 || true
  fi
  if [ "$CONSOLIDATION_STOPPED" = "true" ]; then
    echo "cleanup: restarting consolidationservice (was left stopped)" >&2
    docker compose start consolidationservice >/dev/null 2>&1 || true
  fi
  if [ "$RABBITMQ_STOPPED" = "true" ]; then
    echo "cleanup: restarting rabbitmq (was left stopped)" >&2
    docker compose start rabbitmq >/dev/null 2>&1 || true
  fi
  rm -rf "$TMP_DIR" 2>/dev/null || true
  exit "$rc"
}
trap cleanup EXIT

# ---- helpers ---------------------------------------------------------------

http_post() {
  local url="$1" token="$2" idem_key="$3" data="$4"
  HTTP_STATUS=$(curl -s -o "$BODY_FILE" -D "$HEADERS_FILE" -w "%{http_code}" -X POST "$url" \
    -H "Authorization: Bearer $token" \
    -H "Idempotency-Key: $idem_key" \
    -H "Content-Type: application/json" \
    -d "$data") || HTTP_STATUS="000"
}

http_get() {
  local url="$1" token="$2"
  HTTP_STATUS=$(curl -s -o "$BODY_FILE" -D "$HEADERS_FILE" -w "%{http_code}" "$url" \
    -H "Authorization: Bearer $token") || HTTP_STATUS="000"
}

json_field() {
  python3 -c '
import json, sys
try:
    with open(sys.argv[1]) as f:
        data = json.load(f)
    print(data.get(sys.argv[2], ""))
except Exception:
    print("")
' "$1" "$2"
}

json_last_day_closing() {
  python3 -c '
import json, sys
try:
    with open(sys.argv[1]) as f:
        data = json.load(f)
    days = data.get("days") or []
    print(days[-1].get("closingBalanceInCents", "") if days else "")
except Exception:
    print("")
' "$1"
}

has_header() {
  grep -qi "^$1: *$2" "$HEADERS_FILE"
}

is_nonnegative_int() {
  [[ "$1" =~ ^[0-9]+$ ]]
}

run_step() {
  local name="$1"
  shift
  local t0 t1 elapsed status_label
  t0=$(date +%s)
  if "$@"; then
    status_label="PASS"
  else
    status_label="FAIL"
    OVERALL_FAIL=1
  fi
  t1=$(date +%s)
  elapsed=$((t1 - t0))
  STEP_NAMES+=("$name")
  STEP_STATUS+=("$status_label")
  STEP_ELAPSED+=("${elapsed}s")
  LAST_STEP_STATUS="$status_label"
  echo "[$status_label] $name (${elapsed}s)"
}

skip_step() {
  local name="$1" reason="${2:-dependency failed}"
  STEP_NAMES+=("$name")
  STEP_STATUS+=("SKIP")
  STEP_ELAPSED+=("0s")
  LAST_STEP_STATUS="SKIP"
  echo "[SKIP] $name ($reason)"
}

# ---- step implementations ---------------------------------------------------

step_health_ready() {
  local timeout=90 start elapsed entry_code consolidation_code
  start=$(date +%s)
  while true; do
    entry_code=$(curl -s -o /dev/null -w "%{http_code}" "$ENTRY_BASE/health/ready") || entry_code="000"
    consolidation_code=$(curl -s -o /dev/null -w "%{http_code}" "$CONSOLIDATION_BASE/health/ready") || consolidation_code="000"
    if [ "$entry_code" = "200" ] && [ "$consolidation_code" = "200" ]; then
      return 0
    fi
    elapsed=$(( $(date +%s) - start ))
    if [ "$elapsed" -ge "$timeout" ]; then
      echo "  timeout: CashFlowEntryService /health/ready=$entry_code, ConsolidationService /health/ready=$consolidation_code" >&2
      return 1
    fi
    sleep 2
  done
}

step_capture_baseline() {
  http_get "$CONSOLIDATION_BASE/api/v1/consolidation?fromDate=$TODAY&toDate=$TODAY" "$READ_TOKEN"
  if [ "$HTTP_STATUS" != "200" ]; then
    echo "  failed to capture baseline consolidation totals for $TODAY, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  BASELINE_CREDITS=$(json_field "$BODY_FILE" creditsInCents)
  BASELINE_DEBITS=$(json_field "$BODY_FILE" debitsInCents)
  BASELINE_CLOSING=$(json_field "$BODY_FILE" closingBalanceInCents)
  is_nonnegative_int "$BASELINE_CREDITS" && is_nonnegative_int "$BASELINE_DEBITS" && [[ "$BASELINE_CLOSING" =~ ^-?[0-9]+$ ]]
}

step_happy_path() {
  CREDIT_IDEM_KEY="smoke-credit-$RUN_ID"
  CREDIT_PAYLOAD="{\"amountInCents\":10000,\"nature\":\"Credit\",\"description\":\"smoke credit\"}"
  DEBIT_IDEM_KEY="smoke-debit-$RUN_ID"
  DEBIT_PAYLOAD="{\"amountInCents\":3000,\"nature\":\"Debit\",\"description\":\"smoke debit\"}"

  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$CREDIT_IDEM_KEY" "$CREDIT_PAYLOAD"
  if [ "$HTTP_STATUS" != "201" ]; then
    echo "  credit expected 201, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  CREDIT_ENTRY_ID=$(json_field "$BODY_FILE" entryId)

  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$DEBIT_IDEM_KEY" "$DEBIT_PAYLOAD"
  if [ "$HTTP_STATUS" != "201" ]; then
    echo "  debit expected 201, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  DEBIT_ENTRY_ID=$(json_field "$BODY_FILE" entryId)

  [ -n "$CREDIT_ENTRY_ID" ] && [ -n "$DEBIT_ENTRY_ID" ]
}

step_idempotent_replay() {
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$CREDIT_IDEM_KEY" "$CREDIT_PAYLOAD"
  if [ "$HTTP_STATUS" != "200" ]; then
    echo "  replay expected 200, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  if ! has_header "Idempotency-Replayed" "true"; then
    echo "  replay missing header Idempotency-Replayed: true. Headers: $(cat "$HEADERS_FILE")" >&2
    return 1
  fi
  local replayed_entry_id
  replayed_entry_id=$(json_field "$BODY_FILE" entryId)
  [ "$replayed_entry_id" = "$CREDIT_ENTRY_ID" ]
}

step_payload_mismatch() {
  local mismatched_payload="{\"amountInCents\":99999,\"nature\":\"Credit\",\"description\":\"smoke credit\"}"
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$CREDIT_IDEM_KEY" "$mismatched_payload"
  if [ "$HTTP_STATUS" != "409" ]; then
    echo "  payload mismatch expected 409, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  return 0
}

# Kestrel's own Limits.MaxRequestBodySize (RequestLimitsExtensions.cs) rejects an over-limit body before it
# reaches application code — TestServer (used by the xUnit integration tests) never runs
# Kestrel's connection/stream layer and cannot reproduce this, so this invariant is only
# checked here, against the real container (see InputValidationTests.cs).
step_oversized_body_returns_413() {
  local oversized_description
  oversized_description="$(python3 -c "print('e' * 70000)")"
  local payload="{\"amountInCents\":1000,\"nature\":\"Credit\",\"description\":\"$oversized_description\"}"
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "smoke-oversized-$RUN_ID" "$payload"
  if [ "$HTTP_STATUS" != "413" ]; then
    echo "  oversized body expected 413, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    return 1
  fi
  return 0
}

poll_consolidation() {
  local target_credits="$1" target_debits="$2" target_closing="$3" timeout="$4"
  local start elapsed credits debits closing
  start=$(date +%s)
  while true; do
    http_get "$CONSOLIDATION_BASE/api/v1/consolidation?fromDate=$TODAY&toDate=$TODAY" "$READ_TOKEN"
    if [ "$HTTP_STATUS" = "200" ]; then
      credits=$(json_field "$BODY_FILE" creditsInCents)
      debits=$(json_field "$BODY_FILE" debitsInCents)
      closing=$(json_field "$BODY_FILE" closingBalanceInCents)
      if [ "$credits" = "$target_credits" ] && [ "$debits" = "$target_debits" ] && [ "$closing" = "$target_closing" ]; then
        return 0
      fi
    fi
    elapsed=$(( $(date +%s) - start ))
    if [ "$elapsed" -ge "$timeout" ]; then
      echo "  timeout after ${timeout}s waiting for credits=$target_credits debits=$target_debits closing=$target_closing;" \
           "last response (HTTP $HTTP_STATUS): $(cat "$BODY_FILE" 2>/dev/null)" >&2
      return 1
    fi
    sleep 2
  done
}

step_consolidation_converges() {
  poll_consolidation "$((BASELINE_CREDITS + 10000))" "$((BASELINE_DEBITS + 3000))" "$((BASELINE_CLOSING + 7000))" 30 || return 1

  # BR-02: the last entry of the daily breakdown must agree with the interval's own closing —
  # same running total, just reached via days[] instead of the aggregate fields.
  local interval_closing last_day_closing
  interval_closing=$(json_field "$BODY_FILE" closingBalanceInCents)
  last_day_closing=$(json_last_day_closing "$BODY_FILE")
  if [ "$last_day_closing" != "$interval_closing" ]; then
    echo "  days[] last entry closing ($last_day_closing) does not match closingBalanceInCents ($interval_closing): $(cat "$BODY_FILE")" >&2
    return 1
  fi
}

step_nfr001_consolidation_down() {
  docker compose stop consolidationservice >/dev/null 2>&1 || return 1
  CONSOLIDATION_STOPPED=true

  local idem="smoke-credit2-$RUN_ID"
  local payload="{\"amountInCents\":500,\"nature\":\"Credit\",\"description\":\"smoke credit during consolidation outage\"}"
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$idem" "$payload"
  if [ "$HTTP_STATUS" != "201" ]; then
    echo "  entry while ConsolidationService was down expected 201, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    docker compose start consolidationservice >/dev/null 2>&1 || true
    CONSOLIDATION_STOPPED=false
    return 1
  fi

  echo "  keeping ConsolidationService down for ${OUTAGE_SECONDS}s so the message sits in the queue..." >&2
  sleep "$OUTAGE_SECONDS"

  local queue_messages
  queue_messages=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
    | awk -v q="$CONSOLIDATION_QUEUE_NAME" '$1 == q {print $2}') || queue_messages=""
  if ! is_nonnegative_int "$queue_messages" || [ "$queue_messages" -lt 1 ]; then
    echo "  expected >= 1 message queued on '$CONSOLIDATION_QUEUE_NAME' while ConsolidationService was down, got '$queue_messages'" >&2
    docker compose start consolidationservice >/dev/null 2>&1 || true
    CONSOLIDATION_STOPPED=false
    return 1
  fi
  echo "  confirmed $queue_messages message(s) queued on '$CONSOLIDATION_QUEUE_NAME' while ConsolidationService was down." >&2

  docker compose start consolidationservice >/dev/null 2>&1 || true
  CONSOLIDATION_STOPPED=false

  poll_consolidation "$((BASELINE_CREDITS + 10500))" "$((BASELINE_DEBITS + 3000))" "$((BASELINE_CLOSING + 7500))" 60
}

step_broker_down() {
  docker compose stop rabbitmq >/dev/null 2>&1 || return 1
  RABBITMQ_STOPPED=true

  local idem="smoke-credit3-$RUN_ID"
  local payload="{\"amountInCents\":100,\"nature\":\"Credit\",\"description\":\"smoke credit during broker outage\"}"
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$idem" "$payload"
  if [ "$HTTP_STATUS" != "201" ]; then
    echo "  entry while RabbitMQ was down expected 201, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    docker compose start rabbitmq >/dev/null 2>&1 || true
    RABBITMQ_STOPPED=false
    return 1
  fi
  local entry_id
  entry_id=$(json_field "$BODY_FILE" entryId)

  echo "  keeping RabbitMQ down for ${OUTAGE_SECONDS}s so the Outbox Publisher retries..." >&2
  sleep "$OUTAGE_SECONDS"

  local outbox_row outbox_status outbox_attempts
  outbox_row=$(docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -t -A \
    -c "SELECT status || '|' || attempt_count FROM outbox_messages WHERE aggregate_id = '$entry_id';" 2>/dev/null) || outbox_row=""
  outbox_status="${outbox_row%%|*}"
  outbox_attempts="${outbox_row##*|}"

  if [ -z "$outbox_status" ] || [ "$outbox_status" = "Published" ]; then
    echo "  expected the Outbox row to not be Published while RabbitMQ was down, got status='$outbox_status' (raw: '$outbox_row')" >&2
    docker compose start rabbitmq >/dev/null 2>&1 || true
    RABBITMQ_STOPPED=false
    return 1
  fi
  if ! is_nonnegative_int "$outbox_attempts" || [ "$outbox_attempts" -lt 1 ]; then
    echo "  expected attempt_count >= 1 while RabbitMQ was down, got '$outbox_attempts' (raw: '$outbox_row')" >&2
    docker compose start rabbitmq >/dev/null 2>&1 || true
    RABBITMQ_STOPPED=false
    return 1
  fi
  echo "  confirmed Outbox row status='$outbox_status' attempt_count='$outbox_attempts' while RabbitMQ was down." >&2

  docker compose start rabbitmq >/dev/null 2>&1 || true
  RABBITMQ_STOPPED=false

  poll_consolidation "$((BASELINE_CREDITS + 10600))" "$((BASELINE_DEBITS + 3000))" "$((BASELINE_CLOSING + 7600))" 180
}

# Proves the "invalid event -> DLQ immediately, no retry ladder" path (ADR-002,
# observability/prometheus/alerts.yml DeadLetterQueueNotEmpty, dlq_messages_total): no
# default smoke step above produces a dead-lettered message, since outages only delay
# delivery, they never reject it. Publishes malformed body straight to the broker (bypassing
# the CashFlowEntryService API, the same way tests/ConsolidationService.IntegrationTests/ConsumerTests.cs
# does) so CashFlowEntryRecordedConsumerService.IsValid rejects it on first delivery, then removes the
# one message it created so the queue/alert do not stay dirty after the run.
step_dlq_rejected_message() {
  local dlq_depth_before dlq_depth_after start elapsed publish_status
  dlq_depth_before=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
    | awk -v q="$DLQ_QUEUE_NAME" '$1 == q {print $2}')
  is_nonnegative_int "$dlq_depth_before" || dlq_depth_before=0

  publish_status=$(curl -s -o /dev/null -w "%{http_code}" -u "$RABBITMQ_MGMT_AUTH" \
    -X POST "$RABBITMQ_MGMT_BASE/api/exchanges/%2F/cashflow.events/publish" \
    -H "Content-Type: application/json" \
    -d "{\"properties\":{},\"routing_key\":\"cashflow.entry.recorded.v1\",\"payload\":\"smoke-dlq-not-json-$RUN_ID\",\"payload_encoding\":\"string\"}") \
    || publish_status="000"
  if [ "$publish_status" != "200" ]; then
    echo "  publish to cashflow.events via management API expected 200, got $publish_status" >&2
    return 1
  fi

  start=$(date +%s)
  while true; do
    dlq_depth_after=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
      | awk -v q="$DLQ_QUEUE_NAME" '$1 == q {print $2}')
    is_nonnegative_int "$dlq_depth_after" || dlq_depth_after=""
    if [ -n "$dlq_depth_after" ] && [ "$dlq_depth_after" -ge $((dlq_depth_before + 1)) ]; then
      break
    fi
    elapsed=$(( $(date +%s) - start ))
    if [ "$elapsed" -ge 15 ]; then
      echo "  timeout waiting for $DLQ_QUEUE_NAME depth to reach $((dlq_depth_before + 1)), last seen '$dlq_depth_after'" >&2
      return 1
    fi
    sleep 1
  done
  echo "  confirmed malformed event rejected straight to $DLQ_QUEUE_NAME (depth $dlq_depth_before -> $dlq_depth_after)." >&2

  if [ "$dlq_depth_before" -eq 0 ]; then
    # The DLQ was empty before this step, so its head is exactly the message this step
    # added; safe to remove (ack_requeue_false) so the DLQ/alert settle back to empty.
    curl -s -o /dev/null -u "$RABBITMQ_MGMT_AUTH" \
      -X POST "$RABBITMQ_MGMT_BASE/api/queues/%2F/$DLQ_QUEUE_NAME/get" \
      -H "Content-Type: application/json" \
      -d '{"count":1,"ackmode":"ack_requeue_false","encoding":"auto"}' >/dev/null || true
  else
    echo "  warning: $DLQ_QUEUE_NAME already had $dlq_depth_before message(s) before this step; leaving the head message in place instead of risking removal of a message this step did not publish." >&2
  fi
  return 0
}

step_durability_restart() {
  docker compose stop consolidationservice >/dev/null 2>&1 || return 1
  CONSOLIDATION_STOPPED=true

  local idem="smoke-durability-$RUN_ID"
  local payload="{\"amountInCents\":50,\"nature\":\"Credit\",\"description\":\"smoke credit before compose down/up\"}"
  http_post "$ENTRY_BASE/api/v1/entries" "$WRITE_TOKEN" "$idem" "$payload"
  if [ "$HTTP_STATUS" != "201" ]; then
    echo "  entry before compose down/up expected 201, got $HTTP_STATUS: $(cat "$BODY_FILE")" >&2
    docker compose start consolidationservice >/dev/null 2>&1 || true
    CONSOLIDATION_STOPPED=false
    return 1
  fi
  local entry_id
  entry_id=$(json_field "$BODY_FILE" entryId)

  # Prove the message is actually sitting in RabbitMQ (not just pending in the
  # Outbox) before tearing the stack down: a message
  # already confirmed by the broker must survive a full compose down/up because of
  # the rabbitmq-data volume + fixed hostname, not because it never left Postgres.
  echo "  waiting up to 15s for the Outbox row to be Published and the main queue to hold >= 1 message..." >&2
  local wait_start wait_elapsed outbox_status main_queue_depth confirmed
  confirmed=false
  wait_start=$(date +%s)
  while true; do
    outbox_status=$(docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -t -A \
      -c "SELECT status FROM outbox_messages WHERE aggregate_id = '$entry_id';" 2>/dev/null | tr -d '[:space:]') || outbox_status=""
    main_queue_depth=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
      | awk -v q="$CONSOLIDATION_QUEUE_NAME" '$1 == q {print $2}') || main_queue_depth=""

    if [ "$outbox_status" = "Published" ] && is_nonnegative_int "$main_queue_depth" && [ "$main_queue_depth" -ge 1 ]; then
      confirmed=true
      break
    fi

    wait_elapsed=$(( $(date +%s) - wait_start ))
    if [ "$wait_elapsed" -ge 15 ]; then
      break
    fi
    sleep 1
  done

  echo "  observed before down: outbox status='$outbox_status', main queue ('$CONSOLIDATION_QUEUE_NAME') depth='$main_queue_depth'" >&2
  if [ "$confirmed" != "true" ]; then
    echo "  expected outbox status=Published and main queue depth >= 1 within 15s before tearing the stack down" >&2
    docker compose start consolidationservice >/dev/null 2>&1 || true
    CONSOLIDATION_STOPPED=false
    return 1
  fi

  echo "  docker compose down (no -v)..." >&2
  DURABILITY_DOWN=true
  CONSOLIDATION_STOPPED=false # the whole stack is coming down, not just this service
  if ! docker compose down >/dev/null 2>&1; then
    echo "  docker compose down failed" >&2
    return 1
  fi

  echo "  docker compose up -d..." >&2
  if ! docker compose up -d >/dev/null 2>&1; then
    echo "  docker compose up -d failed" >&2
    return 1
  fi
  DURABILITY_DOWN=false

  if ! step_health_ready; then
    echo "  services did not become ready again after compose down/up" >&2
    return 1
  fi

  local post_up_queue_depth
  post_up_queue_depth=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
    | awk -v q="$CONSOLIDATION_QUEUE_NAME" '$1 == q {print $2}') || post_up_queue_depth=""
  echo "  main queue depth right after compose up (informational — the consumer may already have drained it): '$post_up_queue_depth'" >&2

  poll_consolidation "$((BASELINE_CREDITS + 10650))" "$((BASELINE_DEBITS + 3000))" "$((BASELINE_CLOSING + 7650))" 90
}

step_recovery_reconcile_before() {
  ./scripts/ops/reconcile.sh
}

step_recovery_rebuild() {
  ./scripts/ops/rebuild-consolidation.sh --yes
}

step_recovery_reconcile_after() {
  ./scripts/ops/reconcile.sh
}

# ---- main --------------------------------------------------------------------

echo "CashFlow end-to-end smoke test"
echo "Run: $RUN_ID"
echo "Business date (America/Sao_Paulo): $TODAY"
echo "Outage window per dependency check: ${OUTAGE_SECONDS}s"
[ "$RUN_DURABILITY" = "true" ] && echo "Durability check (--durability): enabled"
[ "$RUN_RECOVERY" = "true" ] && echo "Recovery check (--recovery): enabled"
echo

run_step "health_ready" step_health_ready

WRITE_TOKEN="$(python3 scripts/dev-jwt/make_token.py --scope cashflow.entries.write)"
READ_TOKEN="$(python3 scripts/dev-jwt/make_token.py --scope cashflow.consolidation.read)"

run_step "capture_baseline" step_capture_baseline

if [ "$LAST_STEP_STATUS" = "PASS" ]; then
  run_step "happy_path_credit_and_debit" step_happy_path
else
  skip_step "happy_path_credit_and_debit" "capture_baseline failed"
fi

if [ "$LAST_STEP_STATUS" = "PASS" ]; then
  run_step "idempotent_replay" step_idempotent_replay
  run_step "payload_mismatch_conflict" step_payload_mismatch
  run_step "oversized_body_returns_413" step_oversized_body_returns_413
  run_step "consolidation_converges_7000" step_consolidation_converges
  run_step "nfr001_consolidation_down" step_nfr001_consolidation_down
  run_step "broker_down" step_broker_down
  run_step "dlq_rejected_message" step_dlq_rejected_message
  if [ "$RUN_DURABILITY" = "true" ]; then
    run_step "durability_restart" step_durability_restart
  fi
  if [ "$RUN_RECOVERY" = "true" ]; then
    run_step "recovery_reconcile_before" step_recovery_reconcile_before
    run_step "recovery_rebuild_consolidation" step_recovery_rebuild
    run_step "recovery_reconcile_after" step_recovery_reconcile_after
  fi
else
  skip_step "idempotent_replay" "a previous step failed"
  skip_step "payload_mismatch_conflict" "a previous step failed"
  skip_step "oversized_body_returns_413" "a previous step failed"
  skip_step "consolidation_converges_7000" "a previous step failed"
  skip_step "nfr001_consolidation_down" "a previous step failed"
  skip_step "broker_down" "a previous step failed"
  skip_step "dlq_rejected_message" "a previous step failed"
  if [ "$RUN_DURABILITY" = "true" ]; then
    skip_step "durability_restart" "a previous step failed"
  fi
  if [ "$RUN_RECOVERY" = "true" ]; then
    skip_step "recovery_reconcile_before" "a previous step failed"
    skip_step "recovery_rebuild_consolidation" "a previous step failed"
    skip_step "recovery_reconcile_after" "a previous step failed"
  fi
fi

echo
printf "%-32s %-6s %s\n" "STEP" "RESULT" "ELAPSED"
printf "%-32s %-6s %s\n" "--------------------------------" "------" "-------"
for i in "${!STEP_NAMES[@]}"; do
  printf "%-32s %-6s %s\n" "${STEP_NAMES[$i]}" "${STEP_STATUS[$i]}" "${STEP_ELAPSED[$i]}"
done
echo

if [ "$OVERALL_FAIL" -ne 0 ]; then
  echo "RESULT: FAIL (one or more steps failed)"
  exit 1
fi

echo "RESULT: PASS (all steps passed)"
exit 0
