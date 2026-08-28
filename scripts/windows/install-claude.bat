@echo off
REM Connect Claude Code to the Isis MCP server (via the claude CLI).
REM Authenticates with the credential ACCESS KEY only (sent as the x-access-key header). The secret key is
REM never sent and never leaves your machine; the access key is a capability token, so use a least-privilege one.
REM Usage: install-claude.bat [ACCESS_KEY]  (arg #1 overrides ISIS_ACCESS_KEY)
REM Override defaults with ISIS_MCP_URL / ISIS_ACCESS_KEY.
setlocal
if "%ISIS_MCP_URL%"=="" set "ISIS_MCP_URL=http://127.0.0.1:8720/mcp"
if "%ISIS_ACCESS_KEY%"=="" set "ISIS_ACCESS_KEY=isisdefaultkey"
if not "%~1"=="" set "ISIS_ACCESS_KEY=%~1"

where claude >nul 2>nul
if errorlevel 1 (
  echo Claude CLI not found on PATH. Install Claude Code first: https://docs.anthropic.com/claude-code
  exit /b 1
)

claude mcp add --transport http isis "%ISIS_MCP_URL%" --header "x-access-key: %ISIS_ACCESS_KEY%"
echo Added 'isis' MCP server to Claude Code. Restart Claude Code to pick it up.
endlocal
