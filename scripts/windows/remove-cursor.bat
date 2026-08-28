@echo off
REM Disconnect Cursor from Isis by removing the 'isis' entry from %USERPROFILE%\.cursor\mcp.json.
setlocal
if "%ISIS_CURSOR_CONFIG%"=="" set "ISIS_CURSOR_CONFIG=%USERPROFILE%\.cursor\mcp.json"
set "ISIS_CONFIG=%ISIS_CURSOR_CONFIG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:ISIS_CONFIG; if(-not (Test-Path $p)){ Write-Host ('Nothing to remove at ' + $p); exit }; $raw=Get-Content -Raw -Path $p; if([string]::IsNullOrWhiteSpace($raw)){ exit }; $root=$raw | ConvertFrom-Json; if($root.mcpServers){ $root.mcpServers.PSObject.Properties.Remove('isis') }; [IO.File]::WriteAllText($p, ($root | ConvertTo-Json -Depth 20)); Write-Host ('Removed isis from ' + $p)"
endlocal
