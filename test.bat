@echo off
setlocal
echo === Automated suite (console runner) ===
dotnet run --project src\Test.Automated\Test.Automated.csproj -f net10.0
if errorlevel 1 exit /b 1

echo === xUnit (per-case) ===
dotnet test src\Test.Xunit\Test.Xunit.csproj -f net10.0
if errorlevel 1 exit /b 1

echo === NUnit (per-case) ===
dotnet test src\Test.Nunit\Test.Nunit.csproj -f net10.0
if errorlevel 1 exit /b 1

echo Tests complete.
endlocal
