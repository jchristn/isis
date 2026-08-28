@echo off
REM Connect Mux to the Isis MCP server by adding an 'isis' entry to Mux's mcp-servers.json.
REM
REM Mux can send only ONE auth header, so it authenticates with the credential ACCESS KEY carried as a
REM bearer token (Authorization: Bearer <accessKey>). The access key is the public, transferable material;
REM the secret key is NEVER written here and never leaves your machine. Treat the access key as a capability
REM token and use a least-privilege credential.
REM
REM Usage:  install-mux.bat [ACCESS_KEY]
REM   ACCESS_KEY  optional; overrides ISIS_ACCESS_KEY, which overrides the default 'isisdefaultkey'.
REM Override the endpoint with ISIS_MCP_BASE_URL and the config path with ISIS_MUX_CONFIG.
setlocal
if "%ISIS_MCP_BASE_URL%"=="" set "ISIS_MCP_BASE_URL=http://127.0.0.1:8720"
if "%ISIS_ACCESS_KEY%"=="" set "ISIS_ACCESS_KEY=isisdefaultkey"
if not "%~1"=="" set "ISIS_ACCESS_KEY=%~1"
if "%ISIS_MUX_CONFIG%"=="" set "ISIS_MUX_CONFIG=%USERPROFILE%\.mux\mcp-servers.json"
set "ISIS_CONFIG=%ISIS_MUX_CONFIG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:ISIS_CONFIG; $d=Split-Path -Parent $p; if(-not (Test-Path $d)){ New-Item -ItemType Directory -Force -Path $d | Out-Null }; $raw=''; if(Test-Path $p){ $raw=Get-Content -Raw -Path $p }; if([string]::IsNullOrWhiteSpace($raw)){ $root=[PSCustomObject]@{} } else { $root=$raw | ConvertFrom-Json }; if($null -eq $root.servers){ $root | Add-Member -NotePropertyName servers -NotePropertyValue @() -Force }; $others=@($root.servers | Where-Object { $_.name -ne 'isis' }); $auth=[PSCustomObject]@{ type='bearer'; bearerToken=$env:ISIS_ACCESS_KEY; apiKeyHeader='X-API-Key'; apiKeyValue='' }; $entry=[PSCustomObject]@{ name='isis'; transport='http'; url=$env:ISIS_MCP_BASE_URL; mcpPath='/mcp'; auth=$auth }; $root.servers=@($others + $entry); [IO.File]::WriteAllText($p, ($root | ConvertTo-Json -Depth 20 -Compress)); Write-Host ('Added isis to ' + $p + ' (bearer auth, access key only)')"
echo Point Mux at this file with --mcp-config, or add it to your Mux config directory, then restart Mux.
endlocal
