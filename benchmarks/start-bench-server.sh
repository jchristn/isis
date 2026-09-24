#!/usr/bin/env bash
# Run Isis.Server from the working tree against the benchmark stack (benchmarks/docker/compose.yaml).
# REST on 127.0.0.1:18700, Prometheus metrics on 127.0.0.1:19464. Build first: dotnet build src/Isis.sln -c Release
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$here/.run"
cd "$here/.run"
export ISIS_SETTINGS_FILE=isis.bench.json
export ISIS_REST_PORT=18700 ISIS_REST_HOSTNAME=127.0.0.1
export ISIS_DB_TYPE=Postgresql ISIS_DB_SERVER=127.0.0.1 ISIS_DB_PORT=15432 ISIS_DB_DATABASE=isis ISIS_DB_USERNAME=isis ISIS_DB_PASSWORD=isis
export ISIS_RECALLDB_ENDPOINT=http://127.0.0.1:18600 ISIS_RECALLDB_ADMIN_KEY=recalldbadmin
export ISIS_OBS_ENABLED=true ISIS_OBS_PROM_HOSTNAME=127.0.0.1 ISIS_OBS_PROM_PORT=19464
export ISIS_DEFAULT_ENDPOINT_BASEURL=${ISIS_DEFAULT_ENDPOINT_BASEURL:-http://127.0.0.1:11434}
exec dotnet run --project "$here/../src/Isis.Server" -c Release --no-build
