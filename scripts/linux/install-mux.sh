#!/usr/bin/env sh
# Connect Mux to the Isis MCP server by adding an 'isis' entry to Mux's mcp-servers.json.
#
# Mux can send only ONE auth header, so it authenticates with the credential ACCESS KEY carried as a
# bearer token (Authorization: Bearer <accessKey>). The access key is the public, transferable material;
# the secret key is NEVER written here and never leaves your machine. Treat the access key as a capability
# token and use a least-privilege credential.
#
# Usage:  sh install-mux.sh [ACCESS_KEY]
#   ACCESS_KEY  optional; overrides ISIS_ACCESS_KEY, which overrides the default 'isisdefaultkey'.
# Override the endpoint with ISIS_MCP_BASE_URL and the config path with ISIS_MUX_CONFIG.
set -e

BASE_URL="${ISIS_MCP_BASE_URL:-http://127.0.0.1:8720}"
AK="${1:-${ISIS_ACCESS_KEY:-isisdefaultkey}}"
CONFIG="${ISIS_MUX_CONFIG:-$HOME/.mux/mcp-servers.json}"

command -v python3 >/dev/null 2>&1 || { echo "python3 is required." >&2; exit 1; }

python3 - "$CONFIG" "$BASE_URL" "$AK" <<'PY'
import json, os, sys
path, url, ak = sys.argv[1], sys.argv[2], sys.argv[3]
d = os.path.dirname(path)
if d and not os.path.isdir(d):
    os.makedirs(d, exist_ok=True)
try:
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    if not isinstance(cfg, dict):
        cfg = {}
except Exception:
    cfg = {}
servers = cfg.get("servers")
if not isinstance(servers, list):
    servers = []
servers = [s for s in servers if not (isinstance(s, dict) and s.get("name") == "isis")]
servers.append({"name": "isis", "transport": "http", "url": url, "mcpPath": "/mcp",
                "auth": {"type": "bearer", "bearerToken": ak, "apiKeyHeader": "X-API-Key", "apiKeyValue": ""}})
cfg["servers"] = servers
with open(path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
print("Added 'isis' to " + path + " (bearer auth, access key only)")
PY
echo "Point Mux at this file with --mcp-config, or add it to your Mux config directory, then restart Mux."
