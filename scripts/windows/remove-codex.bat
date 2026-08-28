@echo off
REM Disconnect Codex from Isis by removing the 'isis' entry from %USERPROFILE%\.codex\config.json.
setlocal
if "%ISIS_CODEX_CONFIG%"=="" set "ISIS_CODEX_CONFIG=%USERPROFILE%\.codex\config.json"
set "ISIS_CONFIG=%ISIS_CODEX_CONFIG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:ISIS_CONFIG; if(-not (Test-Path $p)){ Write-Host ('Nothing to remove at ' + $p); exit }; $raw=Get-Content -Raw -Path $p; if([string]::IsNullOrWhiteSpace($raw)){ exit }; $root=$raw | ConvertFrom-Json; if($root.mcpServers){ $root.mcpServers.PSObject.Properties.Remove('isis') }; [IO.File]::WriteAllText($p, ($root | ConvertTo-Json -Depth 20)); Write-Host ('Removed isis from ' + $p)"
endlocal
