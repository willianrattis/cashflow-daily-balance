#!/usr/bin/env bash
# Rebuilds the Consolidation projection from scratch, using CashFlowEntryService's Outbox as the
# replay source (docs/04-schema-logico.md "Rebuild da projeção", FR-007/NFR-009). `cash_flow_entries`
# (the authoritative ledger) is NEVER touched — only the projection's own tables, the
# consolidation queues and the Outbox's publish bookkeeping are reset.
#
# Steps: stop consolidationservice -> truncate inbox_messages/daily_consolidations/
# projection_metadata -> purge the consolidation queues -> reset every outbox_messages row to
# Pending (so CashFlowEntryService's Outbox Publisher republishes everything) -> start
# consolidationservice -> wait until the Outbox has nothing left to publish and the main queue
# is drained -> run reconcile.sh to prove the rebuilt projection matches the ledger.
#
# Uses the admin/superuser role (`cashflow`) on purpose — it is the only role allowed to read
# and write both databases from one script; the services themselves connect as the
# non-superuser `cashflow_entry_svc`/`consolidation_svc` roles (docs/05-aspectos-transversais.md §1).
#
# Usage:
#   scripts/ops/rebuild-consolidation.sh        # asks for confirmation
#   scripts/ops/rebuild-consolidation.sh --yes  # no confirmation (for scripts/runbooks)
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

QUEUE_PREFIX="consolidation.cashflow-entry-recorded.v1"
OUTBOX_WAIT_TIMEOUT="${OUTBOX_WAIT_TIMEOUT:-300}"

if [[ "${1:-}" != "--yes" && "${1:-}" != "-y" ]]; then
  echo "Isto apaga a projeção do Consolidado (Inbox, diário, metadata), esvazia as filas de"
  echo "consolidação e republica TODOS os lançamentos a partir do Outbox. 'cash_flow_entries' não é alterada."
  read -r -p "Continuar? [s/N] " answer
  [[ "$answer" =~ ^[sSyY]$ ]] || { echo "Cancelado."; exit 0; }
fi

echo "Parando consolidationservice..."
docker compose stop consolidationservice >/dev/null

echo "Limpando as tabelas de projeção (inbox_messages, daily_consolidations, projection_metadata)..."
docker compose exec -T postgres psql -U cashflow -d consolidation_db -v ON_ERROR_STOP=1 -q <<'SQL'
TRUNCATE TABLE inbox_messages, daily_consolidations, projection_metadata RESTART IDENTITY CASCADE;
SQL

echo "Esvaziando as filas de consolidação..."
queues=()
while IFS= read -r queue; do
  [[ -n "$queue" ]] && queues+=("$queue")
done < <(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name 2>/dev/null \
  | tr -d '\r' | grep "^${QUEUE_PREFIX}" || true)

if [[ ${#queues[@]} -eq 0 ]]; then
  echo "  nenhuma fila com prefixo $QUEUE_PREFIX encontrada."
else
  for queue in "${queues[@]}"; do
    if docker compose exec -T rabbitmq rabbitmqctl -q purge_queue "$queue" >/dev/null 2>&1; then
      echo "  $queue: vazia"
    else
      echo "  $queue: falha ao esvaziar"
    fi
  done
fi

echo "Marcando todo o Outbox (cashflow_entry_db.outbox_messages) como Pending para republicação..."
docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -v ON_ERROR_STOP=1 -q <<'SQL'
UPDATE outbox_messages
SET status = 'Pending',
    attempt_count = 0,
    next_attempt_at_utc = now(),
    published_at_utc = NULL,
    last_error = NULL;
SQL

echo "Religando consolidationservice..."
docker compose start consolidationservice >/dev/null

echo "Aguardando o Outbox publicar tudo e as filas de consolidação (exceto a DLQ) esvaziarem (timeout ${OUTBOX_WAIT_TIMEOUT}s)..."
# Checa TODAS as filas com o prefixo (principal + cada nível de retry), não só a
# principal: uma mensagem parqueada em retry.N (falha transitória durante o próprio
# rebuild) faria o reconcile.sh rodar cedo demais e falhar. A DLQ é verificada
# separadamente, ao final, como aviso — não bloqueia o rebuild.
start=$(date +%s)
last_status=""
while true; do
  pending=$(docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -t -A \
    -c "SELECT count(*) FROM outbox_messages WHERE status <> 'Published';" 2>/dev/null | tr -d '[:space:]')

  # `rabbitmqctl list_queues` failing or returning no rows (broker down/restarting mid-rebuild)
  # must NOT read as "queues are clear" — that would let the loop exit early and hand
  # reconcile.sh a backlog that was never actually drained. Both cases are treated as "not
  # clear yet" so the loop keeps waiting (until OUTBOX_WAIT_TIMEOUT), printing why.
  if ! queue_list_output=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null); then
    queues_clear="no"
    queues_clear_reason="rabbitmqctl list_queues failed (broker down?)"
  elif [[ -z "$queue_list_output" ]]; then
    queues_clear="no"
    queues_clear_reason="rabbitmqctl list_queues returned no rows (broker down?)"
  else
    queues_clear=$(printf '%s\n' "$queue_list_output" | awk -v prefix="$QUEUE_PREFIX" '
        $1 ~ ("^" prefix) && $1 !~ /\.dlq$/ && $2 != "0" { bad=1 }
        END { print (bad ? "no" : "yes") }
      ')
    queues_clear_reason="queues still have pending messages"
  fi

  if [[ "$pending" == "0" && "$queues_clear" == "yes" ]]; then
    echo "Outbox publicado por completo e filas de consolidação (exceto DLQ) vazias."
    break
  fi

  current_status="Outbox pendente='$pending'; filas claras='$queues_clear' ($queues_clear_reason)"
  if [[ "$current_status" != "$last_status" ]]; then
    echo "  ainda aguardando: $current_status" >&2
    last_status="$current_status"
  fi

  elapsed=$(( $(date +%s) - start ))
  if [[ "$elapsed" -ge "$OUTBOX_WAIT_TIMEOUT" ]]; then
    echo "Timeout de ${OUTBOX_WAIT_TIMEOUT}s: $current_status; profundidade das filas:" >&2
    docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
      | grep "^${QUEUE_PREFIX}" >&2 || true
    exit 1
  fi
  sleep 2
done

dlq_depth=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
  | awk -v q="${QUEUE_PREFIX}.dlq" '$1 == q {print $2}')
if [[ -n "$dlq_depth" && "$dlq_depth" != "0" ]]; then
  echo "AVISO: a DLQ (${QUEUE_PREFIX}.dlq) tem $dlq_depth mensagem(ns) ao final do rebuild." >&2
  echo "       Isso significa que ao menos um evento republicado foi rejeitado de novo" >&2
  echo "       (mensagem permanentemente inválida ou falha inesperada esgotada). O reconcile" >&2
  echo "       a seguir pode divergir por causa desse(s) evento(s) — ver docs/07-runbooks.md §4." >&2
fi

echo "Rodando reconcile.sh para validar a projeção reconstruída..."
./scripts/ops/reconcile.sh
