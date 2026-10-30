#!/usr/bin/env bash
# Orchestrates k6 load runs against the Docker Compose stack and produces versioned evidence
# for NFR-002/NFR-003 (50 req/s, <= 5% loss, Consolidado) and A-013 (50 lançamentos/s).
#
# k6 runs through its official Docker image (pinned tag below), attached to the same network as
# the compose stack, by service name — no local k6 install required.
#
# Usage:
#   scripts/load/run.sh [consolidation|entries|all] [duration]
#     scenario   default: all
#     duration   k6 duration string, default: 2m. A-013 calls for 15 minutes:
#                  scripts/load/run.sh entries 15m
#
# Requires: the compose stack already up (`docker compose up -d`), curl, python3, docker.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

SCENARIO="${1:-all}"
DURATION="${2:-2m}"
K6_IMAGE="grafana/k6:0.54.0"

case "$SCENARIO" in
  consolidation|entries|all) ;;
  *) echo "Usage: $0 [consolidation|entries|all] [duration]" >&2; exit 2 ;;
esac

PROMETHEUS_BASE="${PROMETHEUS_BASE:-http://localhost:9090}"

OUT_DIR="$(mktemp -d)"
CONSOLIDATION_SEEDED=0

cleanup() {
  if [ "$CONSOLIDATION_SEEDED" = "1" ]; then
    echo "Restoring projection consistency: the consolidation scenario seeded synthetic history directly into daily_consolidations, so rebuilding it from the Outbox now removes those rows before reconcile.sh is checked again."
    "$REPO_ROOT/scripts/ops/rebuild-consolidation.sh" --yes
    "$REPO_ROOT/scripts/ops/reconcile.sh"
  fi
  rm -rf "$OUT_DIR"
}
trap cleanup EXIT

EVIDENCE_DIR="docs/evidence/load"
mkdir -p "$EVIDENCE_DIR"
RUN_DATE="$(date +%Y-%m-%d)"

# ---- helpers ----------------------------------------------------------------

network_name() {
  local container
  container="$(docker compose ps -q postgres)"
  if [ -z "$container" ]; then
    echo "postgres is not running — start the stack first: docker compose up -d" >&2
    exit 1
  fi
  docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}' "$container"
}

machine_specs() {
  python3 - <<'PY'
import json, platform, subprocess

def sh(cmd):
    try:
        return subprocess.check_output(cmd, shell=True, text=True).strip()
    except Exception:
        return "?"

os_name = platform.system()
cpu = sh("sysctl -n hw.ncpu") if os_name == "Darwin" else sh("nproc")
mem_raw = sh("sysctl -n hw.memsize") if os_name == "Darwin" else sh("awk '/MemTotal/ {print $2*1024}' /proc/meminfo")
try:
    mem_gib = f"{int(mem_raw) / (1024**3):.1f} GiB"
except Exception:
    mem_gib = "?"

docker_info_raw = sh("docker info --format '{{json .}}'")
try:
    info = json.loads(docker_info_raw)
    docker_cpus = info.get("NCPU", "?")
    docker_mem = info.get("MemTotal", 0)
    docker_mem_gib = f"{int(docker_mem) / (1024**3):.1f} GiB" if docker_mem else "?"
except Exception:
    docker_cpus, docker_mem_gib = "?", "?"

print(f"- Sistema operacional da máquina: {platform.system()} {platform.release()} ({platform.machine()})")
print(f"- Processadores da máquina: {cpu}")
print(f"- Memória da máquina: {mem_gib}")
print(f"- Processadores disponíveis ao Docker: {docker_cpus}")
print(f"- Memória disponível ao Docker: {docker_mem_gib}")
PY
}

k6_metric() {
  # k6_metric <summary.json> <metric> <field>
  python3 -c '
import json, sys
try:
    with open(sys.argv[1]) as f:
        data = json.load(f)
    # k6 --summary-export puts each metric field (p(95), avg, count, rate, value, ...) directly
    # under the metric object — there is no nested "values" key (verified against a live run).
    value = data.get("metrics", {}).get(sys.argv[2], {}).get(sys.argv[3])
    print(value if value is not None else "")
except Exception:
    print("")
' "$1" "$2" "$3"
}

k6_threshold_lines() {
  python3 -c '
import json, sys
with open(sys.argv[1]) as f:
    data = json.load(f)
metrics = data.get("metrics", {})
for name, m in metrics.items():
    thresholds = m.get("thresholds")
    if not thresholds:
        continue
    # Each value is whether the threshold was BREACHED (true = failed), not whether it passed —
    # verified against a live run (e.g. dropped_iterations.count==0 with count=0 reports false).
    for expr, breached in thresholds.items():
        status = "FAIL" if breached else "PASS"
        print(f"- `{name} {expr}` → `{status}`")
' "$1"
}

k6_thresholds_ok() {
  python3 -c '
import json, sys
with open(sys.argv[1]) as f:
    data = json.load(f)
metrics = data.get("metrics", {})
ok = True
for m in metrics.values():
    for breached in (m.get("thresholds") or {}).values():
        if breached:
            ok = False
print("true" if ok else "false")
' "$1"
}

prometheus_quantile() {
  # prometheus_quantile <quantile 0.95|0.99> <range e.g. 5m>
  local q="$1" range="$2" query encoded
  query="histogram_quantile(${q}, sum(rate(consumer_lag_seconds_bucket[${range}])) by (le))"
  encoded="$(python3 -c "import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1]))" "$query")"
  curl -s "${PROMETHEUS_BASE}/api/v1/query?query=${encoded}" | python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
    result = data["data"]["result"]
    print(result[0]["value"][1] if result else "no data")
except Exception as e:
    print(f"error: {e}")
'
}

wait_for_outbox_and_queue_drained() {
  local timeout="${1:-180}" start elapsed pending queue_depth
  start=$(date +%s)
  while true; do
    pending=$(docker compose exec -T postgres psql -U cashflow -d cashflow_entry_db -t -A \
      -c "SELECT count(*) FROM outbox_messages WHERE status <> 'Published';" 2>/dev/null | tr -d '[:space:]') || pending=""
    queue_depth=$(docker compose exec -T rabbitmq rabbitmqctl -q list_queues name messages 2>/dev/null \
      | awk '$1 == "consolidation.cashflow-entry-recorded.v1" {print $2}') || queue_depth=""

    if [ "$pending" = "0" ] && [ "$queue_depth" = "0" ]; then
      return 0
    fi

    elapsed=$(( $(date +%s) - start ))
    if [ "$elapsed" -ge "$timeout" ]; then
      echo "timeout after ${timeout}s waiting for Outbox/queue to drain (pending=$pending, queue=$queue_depth)" >&2
      return 1
    fi
    sleep 2
  done
}

duration_to_seconds() {
  python3 -c "
import re
m = re.match(r'(\d+)([ms])\$', '$1')
n, unit = int(m.group(1)), m.group(2)
print(n * 60 if unit == 'm' else n)
"
}

write_report() {
  local scenario="$1" summary_json="$2" k6_status="$3"
  local p95_lag="${4:-}" p99_lag="${5:-}" drain_status="${6:-0}"
  local report_file="$EVIDENCE_DIR/${RUN_DATE}-${scenario}.md"

  local http_req_duration_p95 http_req_duration_p99 http_req_failed_rate iterations dropped
  http_req_duration_p95=$(k6_metric "$summary_json" http_req_duration "p(95)")
  http_req_duration_p99=$(k6_metric "$summary_json" http_req_duration "p(99)")
  http_req_failed_rate=$(k6_metric "$summary_json" http_req_failed "value")
  iterations=$(k6_metric "$summary_json" iterations "count")
  dropped=$(k6_metric "$summary_json" dropped_iterations "count")

  {
    echo "# Evidência de carga — $scenario ($RUN_DATE)"
    echo
    echo "Gerado por \`scripts/load/run.sh $scenario $DURATION\`."
    echo
    echo "## Especificações da máquina"
    echo
    machine_specs
    echo
    echo "## Parâmetros do cenário"
    echo
    if [ "$scenario" = "consolidation" ]; then
      echo "- Alvo: ConsolidationService \`GET /consolidation\`"
    else
      echo "- Alvo: CashFlowEntryService \`POST /entries\`"
    fi
    echo "- Taxa: 50 requisições por segundo, constantes (\`constant-arrival-rate\`: o k6 inicia 50"
    echo "  requisições a cada segundo, por mais que cada uma demore)"
    echo "- Duração: $DURATION"
    echo "- Ferramenta: k6 (teste de carga), na imagem Docker \`$K6_IMAGE\`"
    echo
    echo "## Resultado"
    echo
    echo "- Requisições feitas (iterações do k6, uma requisição cada): ${iterations:-?}"
    echo "- Requisições que o k6 deixou de enviar por falta de capacidade (\`dropped_iterations\`): ${dropped:-?}"
    echo "- Tempo de resposta, p95 (95% das respostas levaram até este valor): ${http_req_duration_p95:-?} ms"
    echo "- Tempo de resposta, p99 (99% das respostas levaram até este valor): ${http_req_duration_p99:-?} ms"
    if [ "$scenario" = "consolidation" ]; then
      echo "- Proporção de requisições com falha (\`http_req_failed\`): ${http_req_failed_rate:-?}"
    fi
    echo
    echo "### Limites definidos no k6 (thresholds)"
    echo
    k6_threshold_lines "$summary_json"
    echo
    if [ "$scenario" = "entries" ]; then
      echo "## Depois do teste (só no cenário de lançamentos)"
      echo
      if [ "$drain_status" = "0" ]; then
        echo "- O Outbox e a fila principal ficaram vazios depois do teste (confirmado)."
      else
        echo "- **O Outbox ou a fila NÃO ficaram vazios dentro do tempo limite** — veja o log da execução do script."
      fi
      echo "- Atraso do Consolidado (\`consumer_lag_seconds\`), p95 durante o teste: ${p95_lag:-?} s"
      echo "- Atraso do Consolidado (\`consumer_lag_seconds\`), p99 durante o teste: ${p99_lag:-?} s"
      echo
    fi
    echo "## Como ler a \"perda\" de requisições"
    echo
    echo "Lemos os 5% de \"perda\" do PDF como requisições que falham temporariamente — timeout, erro \`5xx\`,"
    echo "conexão recusada —, e nunca como lançamento perdido (premissa A-004, em"
    echo "[\`docs/01-requisitos-e-assumptions.md\`](../../01-requisitos-e-assumptions.md#premissas-assumptions))."
    echo "A proporção \`http_req_failed\` mede exatamente isso."
    echo
    echo "## Resumo bruto do k6 (summary)"
    echo
    echo '```json'
    python3 -m json.tool "$summary_json" 2>/dev/null || echo "{}"
    echo '```'
  } > "$report_file"

  echo "Report: $report_file"

  if [ "$(k6_thresholds_ok "$summary_json")" != "true" ] || [ "$k6_status" != "0" ]; then
    echo "WARNING: at least one k6 threshold failed for '$scenario' — see $report_file" >&2
  fi
}

run_consolidation() {
  echo "== consolidation-50rps ($DURATION) =="
  local today from_date
  today="$(TZ="America/Sao_Paulo" date +%Y-%m-%d)"
  from_date="$(TZ="America/Sao_Paulo" date -v-95d +%Y-%m-%d 2>/dev/null || TZ="America/Sao_Paulo" date -d '95 days ago' +%Y-%m-%d)"

  echo "Seeding 90 days of history..."
  CONSOLIDATION_SEEDED=1
  "$SCRIPT_DIR/seed-consolidation-history.sh" 90

  local token network summary_json k6_status=0
  token="$(python3 scripts/dev-jwt/make_token.py --scope cashflow.consolidation.read)"
  network="$(network_name)"
  summary_json="$OUT_DIR/consolidation-summary.json"

  docker run --rm \
    --network "$network" \
    -v "$REPO_ROOT/tests/load:/scripts:ro" \
    -v "$OUT_DIR:/out" \
    -e BASE_URL="http://consolidationservice:8080" \
    -e TOKEN="$token" \
    -e FROM_DATE="$from_date" \
    -e TO_DATE="$today" \
    -e DURATION="$DURATION" \
    "$K6_IMAGE" run --summary-export=/out/consolidation-summary.json /scripts/consolidation-50rps.js \
    || k6_status=$?

  write_report "consolidation" "$summary_json" "$k6_status"
}

run_entries() {
  echo "== entries-50rps ($DURATION) =="

  local token network summary_json k6_status=0
  token="$(python3 scripts/dev-jwt/make_token.py --scope cashflow.entries.write)"
  network="$(network_name)"
  summary_json="$OUT_DIR/entries-summary.json"

  docker run --rm \
    --network "$network" \
    -v "$REPO_ROOT/tests/load:/scripts:ro" \
    -v "$OUT_DIR:/out" \
    -e BASE_URL="http://cashflowentryservice:8080" \
    -e TOKEN="$token" \
    -e DURATION="$DURATION" \
    "$K6_IMAGE" run --summary-export=/out/entries-summary.json /scripts/entries-50rps.js \
    || k6_status=$?

  echo "Waiting for the Outbox and the main queue to drain..."
  local drain_status=0
  wait_for_outbox_and_queue_drained 180 || drain_status=$?

  echo "Querying Prometheus for consumer_lag_seconds p95/p99 over the run..."
  local duration_seconds range p95 p99
  duration_seconds="$(duration_to_seconds "$DURATION")"
  range="$(( duration_seconds + 60 ))s"
  p95="$(prometheus_quantile 0.95 "$range")"
  p99="$(prometheus_quantile 0.99 "$range")"

  write_report "entries" "$summary_json" "$k6_status" "$p95" "$p99" "$drain_status"
}

if [ "$SCENARIO" = "consolidation" ] || [ "$SCENARIO" = "all" ]; then
  run_consolidation
fi
if [ "$SCENARIO" = "entries" ] || [ "$SCENARIO" = "all" ]; then
  run_entries
fi
