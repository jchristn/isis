#!/bin/sh
# reset.sh - Reset the Isis docker environment to factory defaults.
#
# Destroys all runtime docker data (Postgres, RecallDB, Prometheus, Tempo, Loki, Alloy, Grafana volumes)
# and clears local logs, leaving the stack ready for a fresh seeded "docker compose ... up".

set -u

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
echo
printf "Type 'RESET' to confirm: "
read CONFIRM
echo

if [ "${CONFIRM}" != "RESET" ]; then
  echo "Aborted. No changes were made."
  exit 1
fi

echo "[1/2] Stopping containers and removing volumes..."
cd "${DOCKER_DIR}"
docker compose -f compose.yaml -f factory/compose.factory.yaml down -v 2>/dev/null || true
docker compose down -v 2>/dev/null || true

echo "[2/2] Clearing local logs..."
rm -rf "${DOCKER_DIR}/logs"

echo
echo "Factory reset complete. To start the seeded demo environment:"
echo "  cd ${DOCKER_DIR}"
echo "  docker compose -f compose.yaml -f factory/compose.factory.yaml up -d --build"
echo
