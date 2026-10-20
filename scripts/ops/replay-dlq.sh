#!/usr/bin/env bash
# Replays messages from the Dead Letter Queue back onto the main consumption queue, using a
# dynamic RabbitMQ shovel (rabbitmq_shovel/rabbitmq_shovel_management, enabled in
# scripts/rabbitmq/enabled_plugins) instead of a custom consumer — the broker moves the
# messages itself.
#
# `src-delete-after: queue-length` tells the shovel to move exactly as many messages as were
# in the DLQ when it started, then stop and delete itself; it will NOT pick up messages that
# land in the DLQ *while* it is running. Re-run this script if the DLQ keeps growing.
#
# Replayed messages are deduplicated downstream by the Inbox (EventId/LaunchId, ADR-004): a
# message that was valid all along is applied once even if replayed more than once; an
# invalid message is rejected by the consumer the same way it was the first time and lands
# right back in the DLQ — that is expected, not a bug.
# This script therefore waits for the SHOVEL to finish moving its captured batch, not for the
# DLQ to end up empty — whether messages stay away is a data question, not an execution one.
#
# Uses the RabbitMQ administrative user (`cashflow`, the broker's only credential in this local
# stack) on purpose: creating a shovel is a management-API operation that the services never
# perform (docs/05-aspectos-transversais.md §1).
#
# Usage: scripts/ops/replay-dlq.sh [timeout_seconds]   (default 60)
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

MGMT_URL="http://localhost:15672"
MGMT_USER="cashflow"
MGMT_PASS="cashflow-dev-only"
VHOST="%2F"
SHOVEL_NAME="dlq-replay-$(date +%s)"
SRC_QUEUE="consolidation.launch-recorded.v1.dlq"
DEST_QUEUE="consolidation.launch-recorded.v1"
TIMEOUT="${1:-60}"

cleanup() {
  curl -s -u "$MGMT_USER:$MGMT_PASS" -X DELETE \
    "$MGMT_URL/api/parameters/shovel/$VHOST/$SHOVEL_NAME" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# rabbitmqctl talks to the live broker directly; the management HTTP API's per-object stats
# are refreshed periodically, not in real time, so they are not reliable for a tight wait loop.
dlq_depth() {
  docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
    | awk -v q="$SRC_QUEUE" '$1 == q {print $2}'
}

depth=$(dlq_depth)
if [[ -z "$depth" || "$depth" == "0" ]]; then
  echo "DLQ ($SRC_QUEUE) já está vazia; nada para repetir."
  exit 0
fi

echo "DLQ ($SRC_QUEUE) tem $depth mensagem(ns). Criando shovel dinâmico $SHOVEL_NAME..."

SHOVEL_BODY=$(cat <<JSON
{
  "value": {
    "src-protocol": "amqp091",
    "src-uri": "amqp://$MGMT_USER:$MGMT_PASS@rabbitmq:5672/%2F",
    "src-queue": "$SRC_QUEUE",
    "dest-protocol": "amqp091",
    "dest-uri": "amqp://$MGMT_USER:$MGMT_PASS@rabbitmq:5672/%2F",
    "dest-queue": "$DEST_QUEUE",
    "src-delete-after": "queue-length",
    "ack-mode": "on-confirm"
  }
}
JSON
)

response_file="$(mktemp)"
http_status=$(curl -s -o "$response_file" -w "%{http_code}" \
  -u "$MGMT_USER:$MGMT_PASS" -X PUT "$MGMT_URL/api/parameters/shovel/$VHOST/$SHOVEL_NAME" \
  -H "Content-Type: application/json" -d "$SHOVEL_BODY")
response_body=$(cat "$response_file" 2>/dev/null || true)
rm -f "$response_file"

if [[ "$http_status" != "201" && "$http_status" != "204" ]]; then
  echo "Falha ao criar o shovel (HTTP $http_status): $response_body" >&2
  exit 1
fi

shovel_still_present() {
  curl -s -u "$MGMT_USER:$MGMT_PASS" "$MGMT_URL/api/shovels/$VHOST" 2>/dev/null \
    | python3 -c "
import json, sys
try:
    shovels = json.load(sys.stdin)
    print('yes' if any(s.get('name') == sys.argv[1] for s in shovels) else 'no')
except Exception:
    print('yes')
" "$SHOVEL_NAME"
}

echo "Shovel criado. Aguardando o shovel terminar de mover o lote capturado (timeout ${TIMEOUT}s)..."
start=$(date +%s)
while true; do
  if [[ "$(shovel_still_present)" == "no" ]]; then
    echo "Shovel concluído e removido (src-delete-after: queue-length)."
    break
  fi

  elapsed=$(( $(date +%s) - start ))
  if [[ "$elapsed" -ge "$TIMEOUT" ]]; then
    echo "Timeout de ${TIMEOUT}s esperando o shovel terminar (ainda presente na lista)." >&2
    exit 1
  fi
  sleep 1
done

final_depth=$(dlq_depth)
echo "Profundidade da DLQ depois do replay: ${final_depth:-0}"
if [[ -z "$final_depth" || "$final_depth" == "0" ]]; then
  echo "Replay concluído: DLQ vazia — as mensagens movidas foram reprocessadas com sucesso."
else
  echo "Replay concluído: $final_depth mensagem(ns) voltaram à DLQ — o consumer as rejeitou de novo" \
       "(comportamento esperado para eventos permanentemente inválidos; não é uma falha deste script)."
fi
exit 0
