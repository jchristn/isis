@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-mcp.bat ^<tag^>
    echo Example: build-mcp.bat v0.1.0
    exit /b 1
)

set TAG=%~1
set IMAGE=jchristn77/isis-mcp

pushd "%~dp0"

echo Building %IMAGE%:latest and %IMAGE%:%TAG%...
docker buildx build ^
    --builder cloud-jchristn77-jchristn77 ^
    --platform linux/amd64,linux/arm64/v8 ^
    -t %IMAGE%:latest ^
    -t %IMAGE%:%TAG% ^
    -f docker/mcp/Dockerfile ^
    --push ^
    .
set EXIT_CODE=%ERRORLEVEL%

echo Done.
popd
endlocal
exit /b %EXIT_CODE%
