@echo off
REM Connect the Gemini CLI to Isis by adding an 'isis' entry to %USERPROFILE%\.gemini\settings.json.
REM Gemini addresses streamable-HTTP servers with the "httpUrl" field.
REM Authenticates with the credential ACCESS KEY only (x-access-key header); the secret key is never sent.
REM Usage: install-gemini.bat [ACCESS_KEY]  (arg #1 overrides ISIS_ACCESS_KEY)
REM Override with ISIS_MCP_URL / ISIS_ACCESS_KEY / ISIS_GEMINI_CONFIG.
setlocal
if "%ISIS_MCP_URL%"=="" set "ISIS_MCP_URL=http://127.0.0.1:8720/mcp"
if "%ISIS_ACCESS_KEY%"=="" set "ISIS_ACCESS_KEY=isisdefaultkey"
if not "%~1"=="" set "ISIS_ACCESS_KEY=%~1"
if "%ISIS_GEMINI_CONFIG%"=="" set "ISIS_GEMINI_CONFIG=%USERPROFILE%\.gemini\settings.json"
set "ISIS_CONFIG=%ISIS_GEMINI_CONFIG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:ISIS_CONFIG; $d=Split-Path -Parent $p; if(-not (Test-Path $d)){ New-Item -ItemType Directory -Force -Path $d | Out-Null }; $raw=''; if(Test-Path $p){ $raw=Get-Content -Raw -Path $p }; if([string]::IsNullOrWhiteSpace($raw)){ $root=[PSCustomObject]@{} } else { $root=$raw | ConvertFrom-Json }; if($null -eq $root.mcpServers){ $root | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([PSCustomObject]@{}) -Force }; $h=[PSCustomObject]@{ 'x-access-key'=$env:ISIS_ACCESS_KEY }; $entry=[PSCustomObject]@{ httpUrl=$env:ISIS_MCP_URL; headers=$h }; $root.mcpServers | Add-Member -NotePropertyName isis -NotePropertyValue $entry -Force; [IO.File]::WriteAllText($p, ($root | ConvertTo-Json -Depth 20)); Write-Host ('Added isis to ' + $p)"
echo Restart the Gemini CLI to pick up the change (run /mcp to verify).
endlocal
