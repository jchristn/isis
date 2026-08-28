#!/bin/sh
# reset.sh - Reset the Isis docker environment to factory defaults.
#
# Destroys all runtime docker data (Postgres, RecallDB, Prometheus, Tempo, Loki, Alloy, Grafana volumes)
# and clears local logs, leaving the stack ready for a fresh seeded "docker compose ... up".
#
# Usage: reset.sh [--no-ollama]
#   --no-ollama   Preserve the Ollama model volume so gemma3:4b / all-minilm are not re-downloaded.

set -u

NO_OLLAMA=0
for arg in "$@"; do
  case "$arg" in
    --no-ollama) NO_OLLAMA=1 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DOCKER_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo
echo "=========================================================="
echo "  Isis - Reset to Factory Defaults"
echo "=========================================================="
echo
echo "WARNING: This is DESTRUCTIVE. All docker volumes (Postgres with the isis"
echo "and recalldb databases, RecallDB, and the observability stack) and the"
echo "local logs directory will be deleted."
if [ "${NO_OLLAMA}" = "1" ]; then
  echo
  echo "  (--no-ollama) The Ollama model volume will be PRESERVED."
fi
echo
printf "Type 'RESET' to confirm: "
read CONFIRM
echo

if [ "${CONFIRM}" != "RESET" ]; then
  echo "Aborted. No changes were made."
  exit 1
fi

cd "${DOCKER_DIR}"

if [ "${NO_OLLAMA}" = "1" ]; then
  echo "[1/2] Stopping containers (preserving the Ollama model volume)..."
  docker compose -f compose.yaml -f factory/compose.factory.yaml down 2>/dev/null || true
  docker compose down 2>/dev/null || true
  # Remove every Isis volume except the Ollama model cache.
  for v in $(docker volume ls --format '{{.Name}}' | grep -i isis | grep -vi ollama); do
    docker volume rm "$v" >/dev/null 2>&1 || true
  done
else
  echo "[1/2] Stopping containers and removing volumes..."
  docker compose -f compose.yaml -f factory/compose.factory.yaml down -v 2>/dev/null || true
  docker compose down -v 2>/dev/null || true
fi

echo "[2/2] Clearing local logs..."
rm -rf "${DOCKER_DIR}/logs"

echo
echo "Factory reset complete. To start the seeded demo environment:"
echo "  cd ${DOCKER_DIR}"
echo "  docker compose -f compose.yaml -f factory/compose.factory.yaml up -d --build"
echo
