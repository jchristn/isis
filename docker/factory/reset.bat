@echo off
setlocal

REM ==========================================================================
REM reset.bat - Reset the Isis docker environment to factory defaults.
REM
REM Destroys all runtime docker data (Postgres, RecallDB, Prometheus, Tempo,
REM Loki, Alloy, Grafana volumes) and clears local logs, leaving the stack
REM ready for a fresh seeded "docker compose ... up".
REM ==========================================================================

set "SCRIPT_DIR=%~dp0"
set "DOCKER_DIR=%SCRIPT_DIR%..\"

echo.
echo ==========================================================
echo   Isis - Reset to Factory Defaults
echo ==========================================================
echo.
echo WARNING: This is DESTRUCTIVE. All docker volumes (Postgres with the isis
echo and recalldb databases, RecallDB, and the observability stack) and the
echo local logs directory will be deleted.
echo.
set /p "CONFIRM=Type 'RESET' to confirm: "
echo.

if not "%CONFIRM%"=="RESET" (
    echo Aborted. No changes were made.
    exit /b 1
)

echo [1/2] Stopping containers and removing volumes...
pushd "%DOCKER_DIR%"
docker compose -f compose.yaml -f factory\compose.factory.yaml down -v 2>nul
docker compose down -v 2>nul
popd

echo [2/2] Clearing local logs...
rd /s /q "%DOCKER_DIR%logs" 2>nul

echo.
echo Factory reset complete. To start the seeded demo environment:
echo   cd %DOCKER_DIR%
echo   docker compose -f compose.yaml -f factory\compose.factory.yaml up -d --build
echo.

endlocal
