@echo off
REM Disconnect Claude Code from the Isis MCP server (via the claude CLI).
setlocal
where claude >nul 2>nul
if errorlevel 1 (
  echo Claude CLI not found on PATH.
  exit /b 1
)

claude mcp remove isis
echo Removed 'isis' MCP server from Claude Code.
endlocal
