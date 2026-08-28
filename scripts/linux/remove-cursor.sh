#!/usr/bin/env sh
# Disconnect Cursor from Isis by removing the 'isis' entry from ~/.cursor/mcp.json.
set -e

CONFIG="${ISIS_CURSOR_CONFIG:-$HOME/.cursor/mcp.json}"
command -v python3 >/dev/null 2>&1 || { echo "python3 is required." >&2; exit 1; }

python3 - "$CONFIG" <<'PY'
import json, sys
path = sys.argv[1]
try:
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
except Exception:
    print("Nothing to remove at " + path)
    raise SystemExit(0)
if isinstance(cfg, dict) and isinstance(cfg.get("mcpServers"), dict) and "isis" in cfg["mcpServers"]:
    cfg["mcpServers"].pop("isis", None)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2)
    print("Removed 'isis' from " + path)
else:
    print("No 'isis' entry found in " + path)
PY
