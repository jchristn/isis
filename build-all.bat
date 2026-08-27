@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-all.bat ^<tag^>
    echo Example: build-all.bat v0.1.0
    endlocal
    exit /b 1
)

set TAG=%~1

pushd "%~dp0"

call build-server.bat "%TAG%"
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" ( popd & endlocal & exit /b %EXIT_CODE% )

call build-mcp.bat "%TAG%"
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" ( popd & endlocal & exit /b %EXIT_CODE% )

call build-dashboard.bat "%TAG%"
set EXIT_CODE=%ERRORLEVEL%
if not "%EXIT_CODE%"=="0" ( popd & endlocal & exit /b %EXIT_CODE% )

echo Done.
popd
endlocal
exit /b 0
