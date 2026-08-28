#!/usr/bin/env sh
# Disconnect Claude Code from the Isis MCP server (via the claude CLI).
set -e

if ! command -v claude >/dev/null 2>&1; then
  echo "Claude CLI not found on PATH." >&2
  exit 1
fi

claude mcp remove isis
echo "Removed 'isis' MCP server from Claude Code."
