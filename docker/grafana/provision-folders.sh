#!/bin/sh
# Grafana 13's file provisioner (foldersFromFilesStructure) only ever creates a single flat folder per
# dashboard from its immediate parent directory — it cannot build a nested folder hierarchy, and the
# provider `folder` field can't nest either. So we provision the structure through the Grafana HTTP API:
# a single top-level "Isis" folder with one nested subfolder per category, and upload each dashboard into
# its subfolder. Idempotent — safe to re-run (folder creates that conflict are ignored; dashboards use
# overwrite:true). Runs as a one-shot container after Grafana is healthy.
set -eu

GRAFANA_URL="${GRAFANA_URL:-http://grafana:3000}"
GRAFANA_USER="${GRAFANA_USER:-admin}"
GRAFANA_PASS="${GRAFANA_PASS:-admin}"
AUTH="-u ${GRAFANA_USER}:${GRAFANA_PASS}"
DASHBOARD_DIR="${DASHBOARD_DIR:-/dashboards}"

echo "Waiting for Grafana at ${GRAFANA_URL} ..."
i=0
while [ "$i" -lt 60 ]; do
  if curl -fsS $AUTH "${GRAFANA_URL}/api/health" >/dev/null 2>&1; then break; fi
  i=$((i + 1))
  sleep 2
done
if [ "$i" -ge 60 ]; then echo "Grafana did not become ready in time" >&2; exit 1; fi

# Create a folder (idempotent): a 409/412 (already exists) is treated as success.
create_folder() {
  uid="$1"; title="$2"; parent="$3"
  if [ -n "$parent" ]; then
    body="{\"uid\":\"${uid}\",\"title\":\"${title}\",\"parentUid\":\"${parent}\"}"
  else
    body="{\"uid\":\"${uid}\",\"title\":\"${title}\"}"
  fi
  code=$(curl -s -o /dev/null -w "%{http_code}" $AUTH -H "Content-Type: application/json" \
    -X POST "${GRAFANA_URL}/api/folders" -d "$body")
  case "$code" in
    200|201|409|412) echo "  folder '${title}' (uid=${uid}) -> ${code}" ;;
    *) echo "  folder '${title}' (uid=${uid}) FAILED -> ${code}" >&2; return 1 ;;
  esac
}

# Upload a dashboard model into a folder (overwrite:true so re-runs update in place).
upload_dashboard() {
  file="$1"; folder_uid="$2"
  [ -f "$file" ] || { echo "  missing dashboard file: $file" >&2; return 1; }
  model=$(cat "$file")
  body="{\"dashboard\":${model},\"folderUid\":\"${folder_uid}\",\"overwrite\":true}"
  code=$(curl -s -o /dev/null -w "%{http_code}" $AUTH -H "Content-Type: application/json" \
    -X POST "${GRAFANA_URL}/api/dashboards/db" -d "$body")
  case "$code" in
    200) echo "  dashboard '$(basename "$file")' -> ${folder_uid} (${code})" ;;
    *) echo "  dashboard '$(basename "$file")' FAILED -> ${code}" >&2; return 1 ;;
  esac
}

echo "Provisioning Isis folder tree ..."
create_folder "isis" "Isis" ""

# category dir -> subfolder title. Order defines display; each becomes a nested child of Isis.
for entry in "Overview:Overview" "API:API" "Runtime:Runtime" "Services:Services" "Storage:Storage"; do
  dir="${entry%%:*}"; title="${entry##*:}"
  child_uid="isis-folder-$(echo "$dir" | tr '[:upper:]' '[:lower:]')"
  create_folder "$child_uid" "$title" "isis"
  for f in "${DASHBOARD_DIR}/${dir}"/*.json; do
    [ -e "$f" ] || continue
    upload_dashboard "$f" "$child_uid"
  done
done

echo "Grafana folder provisioning complete."
