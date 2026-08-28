#!/usr/bin/env sh
# Connect Claude Code to the Isis MCP server (via the claude CLI).
# Authenticates with the credential ACCESS KEY only (sent as the x-access-key header). The secret key is
# never sent and never leaves your machine; the access key is a capability token, so use a least-privilege one.
# Usage: install-claude.sh [ACCESS_KEY]  (arg #1 overrides ISIS_ACCESS_KEY)
# Override defaults with ISIS_MCP_URL / ISIS_ACCESS_KEY.
set -e

URL="${ISIS_MCP_URL:-http://127.0.0.1:8720/mcp}"
AK="${1:-${ISIS_ACCESS_KEY:-isisdefaultkey}}"

if ! command -v claude >/dev/null 2>&1; then
  echo "Claude CLI not found on PATH. Install Claude Code first: https://docs.anthropic.com/claude-code" >&2
  exit 1
fi

claude mcp add --transport http isis "$URL" --header "x-access-key: $AK"
echo "Added 'isis' MCP server to Claude Code. Restart Claude Code to pick it up."
