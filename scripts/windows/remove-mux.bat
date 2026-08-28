@echo off
REM Disconnect Mux from Isis by removing the 'isis' entry from Mux's mcp-servers.json.
setlocal
if "%ISIS_MUX_CONFIG%"=="" set "ISIS_MUX_CONFIG=%USERPROFILE%\.mux\mcp-servers.json"
set "ISIS_CONFIG=%ISIS_MUX_CONFIG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:ISIS_CONFIG; if(-not (Test-Path $p)){ Write-Host ('Nothing to remove at ' + $p); exit }; $raw=Get-Content -Raw -Path $p; if([string]::IsNullOrWhiteSpace($raw)){ exit }; $root=$raw | ConvertFrom-Json; if($root.servers){ $root.servers=@($root.servers | Where-Object { $_.name -ne 'isis' }) }; [IO.File]::WriteAllText($p, ($root | ConvertTo-Json -Depth 20)); Write-Host ('Removed isis from ' + $p)"
endlocal
