#!/usr/bin/env bash
# Run Isis.McpServer from the working tree, proxying the benchmark REST server (start-bench-server.sh).
# MCP on http://127.0.0.1:18720/mcp. Used by the agent benchmark.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$here/.run"
cd "$here/.run"
export ISIS_MCP_SETTINGS_FILE=isis-mcp.bench.json
export ISIS_MCP_HOSTNAME=127.0.0.1 ISIS_MCP_PORT=18720
export ISIS_MCP_REST_HOSTNAME=127.0.0.1 ISIS_MCP_REST_PORT=18700
exec dotnet run --project "$here/../src/Isis.McpServer" -c Release --no-build
