#!/usr/bin/env bash
# Renders the Structurizr DSL model (docs/diagrams/structurizr/workspace.dsl) to SVG, one file
# per view, into docs/diagrams/svg/.
#
# Pipeline (Docker only, no local Java/Structurizr/PlantUML install required):
#   1. structurizr/structurizr `export -f plantuml/c4plantuml` turns each view into a .puml file.
#      The image offers no direct svg/png export format without running a local Structurizr
#      server + headless browser (`-url`); the plantuml/c4plantuml export is the documented,
#      scriptable path (docs/diagrams/README.md).
#   2. plantuml/plantuml `--svg` renders each .puml to .svg.
#
# Both image tags are pinned by digest (below) so re-running this script on the same DSL always
# produces byte-identical SVGs — the acceptance criterion is "no git diff" on a second run.
#
# Usage:
#   scripts/diagrams/render.sh
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

STRUCTURIZR_IMAGE="structurizr/structurizr:2026.09.19"
STRUCTURIZR_DIGEST="sha256:721136283c2f9cf1ba69037bc9de136c579d66fdcb2d771cb60546ec68def1a5"
PLANTUML_IMAGE="plantuml/plantuml:1.2026.9"
PLANTUML_DIGEST="sha256:9b5071ab56a4192aac458ab4b0d42261c22c8109695894ae5be6013c3be6cac1"

MODEL_DIR="$PWD/docs/diagrams/structurizr"
SVG_DIR="$PWD/docs/diagrams/svg"
WORK_DIR="$(mktemp -d)"
# O comando `mktemp -d` cria a pasta com permissões 0700. No Linux (o ambiente de execução de CI),
# os contêineres abaixo são executados com um usuário próprio que não possui privilégios de root
# — diferente do usuário que executa o processo de CI —, por isso eles precisam de permissão de
# escrita na pasta. O Docker Desktop no macOS não impõe as permissões do sistema host; é por
# esse motivo que o problema ocorre apenas no ambiente de CI.
chmod 0777 "$WORK_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

for pinned in "$STRUCTURIZR_IMAGE@$STRUCTURIZR_DIGEST" "$PLANTUML_IMAGE@$PLANTUML_DIGEST"; do
  echo "pulling $pinned"
  docker pull --quiet "$pinned" >/dev/null
done

echo "validating workspace.dsl"
docker run --rm \
  -v "$MODEL_DIR:/usr/local/structurizr" \
  "$STRUCTURIZR_IMAGE@$STRUCTURIZR_DIGEST" \
  validate -w /usr/local/structurizr/workspace.dsl

echo "exporting views to plantuml/c4plantuml"
docker run --rm \
  -v "$MODEL_DIR:/usr/local/structurizr" \
  -v "$WORK_DIR:/usr/local/out" \
  "$STRUCTURIZR_IMAGE@$STRUCTURIZR_DIGEST" \
  export -w /usr/local/structurizr/workspace.dsl -f plantuml/c4plantuml -o /usr/local/out

echo "rendering plantuml to SVG"
docker run --rm \
  -v "$WORK_DIR:/data" \
  "$PLANTUML_IMAGE@$PLANTUML_DIGEST" \
  --svg -o /data /data/structurizr-*.puml

mkdir -p "$SVG_DIR"
rm -f "$SVG_DIR"/*.svg
for svg in "$WORK_DIR"/structurizr-*.svg; do
  name="$(basename "$svg")"
  name="${name#structurizr-}"
  cp "$svg" "$SVG_DIR/$name"
  echo "wrote docs/diagrams/svg/$name"
done
