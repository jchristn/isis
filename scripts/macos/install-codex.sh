#!/usr/bin/env sh
# Connect Codex to the Isis MCP server by adding an 'isis' entry to ~/.codex/config.json.
# Authenticates with the credential ACCESS KEY only (x-access-key header); the secret key is never sent.
# Usage: install-codex.sh [ACCESS_KEY]  (arg #1 overrides ISIS_ACCESS_KEY)
# Override with ISIS_MCP_URL / ISIS_ACCESS_KEY / ISIS_CODEX_CONFIG.
set -e

URL="${ISIS_MCP_URL:-http://127.0.0.1:8720/mcp}"
AK="${1:-${ISIS_ACCESS_KEY:-isisdefaultkey}}"
CONFIG="${ISIS_CODEX_CONFIG:-$HOME/.codex/config.json}"

command -v python3 >/dev/null 2>&1 || { echo "python3 is required." >&2; exit 1; }

python3 - "$CONFIG" "$URL" "$AK" <<'PY'
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
servers = cfg.get("mcpServers")
if not isinstance(servers, dict):
    servers = {}
    cfg["mcpServers"] = servers
servers["isis"] = {"type": "http", "url": url, "headers": {"x-access-key": ak}}
with open(path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
print("Added 'isis' to " + path)
PY
echo "Restart Codex to pick up the change."
