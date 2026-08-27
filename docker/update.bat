@echo off
setlocal

REM ==========================================================================
REM update.bat - Pull the latest published images and recreate the stack.
REM
REM Pulls the newest jchristn77/isis-* images, tears the running stack down,
REM brings it back up (detached) on the freshly pulled images, and prints the
REM final container status. Non-destructive: named volumes are preserved.
REM ==========================================================================

set "SCRIPT_DIR=%~dp0"

echo.
echo ==========================================================
echo   Isis - Update Docker Stack
echo ==========================================================
echo.

pushd "%SCRIPT_DIR%"

echo [1/4] Pulling latest images...
docker compose pull || goto :fail

echo [2/4] Stopping containers...
docker compose down || goto :fail

echo [3/4] Starting containers...
docker compose up -d || goto :fail

echo [4/4] Container status:
docker ps -a

popd
echo.
echo Update complete.
echo.
endlocal
exit /b 0

:fail
popd
echo.
echo Update FAILED. See the output above.
echo.
endlocal
exit /b 1
