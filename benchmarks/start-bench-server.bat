@echo off
REM Run Isis.Server from the working tree against the benchmark stack (benchmarks\docker\compose.yaml).
REM REST on 127.0.0.1:18700, Prometheus metrics on 127.0.0.1:19464. Build first: dotnet build src\Isis.sln -c Release
setlocal
if not exist "%~dp0.run" mkdir "%~dp0.run"
pushd "%~dp0.run"
set ISIS_SETTINGS_FILE=isis.bench.json
set ISIS_REST_PORT=18700
set ISIS_REST_HOSTNAME=127.0.0.1
set ISIS_DB_TYPE=Postgresql
set ISIS_DB_SERVER=127.0.0.1
set ISIS_DB_PORT=15432
set ISIS_DB_DATABASE=isis
set ISIS_DB_USERNAME=isis
set ISIS_DB_PASSWORD=isis
set ISIS_RECALLDB_ENDPOINT=http://127.0.0.1:18600
set ISIS_RECALLDB_ADMIN_KEY=recalldbadmin
set ISIS_OBS_ENABLED=true
set ISIS_OBS_PROM_HOSTNAME=127.0.0.1
set ISIS_OBS_PROM_PORT=19464
if "%ISIS_DEFAULT_ENDPOINT_BASEURL%"=="" set ISIS_DEFAULT_ENDPOINT_BASEURL=http://127.0.0.1:11434
dotnet run --project "%~dp0..\src\Isis.Server" -c Release --no-build
popd
endlocal
