@echo off
REM Run the standard Isis benchmark suite against the bench stack (see README.md for setup).
REM   benchmarks\run-baseline.bat            retrieval + load + chat (no API spend)
REM   benchmarks\run-baseline.bat --agent    also run the agent benchmark (spends Claude API credits)
REM Results land in benchmarks\results\. Scopes are reused between runs; set REINGEST=1 to rebuild them.
setlocal
pushd "%~dp0.."
set EXTRA=
if "%REINGEST%"=="1" set EXTRA=--reingest
if "%AGENT_MODEL%"=="" set AGENT_MODEL=haiku
set B=dotnet run --project src\Test.Benchmark -c Release --no-build --

%B% retrieval --dataset benchmarks\datasets\isis-live.json %EXTRA%
%B% retrieval --dataset benchmarks\datasets\atlas.json %EXTRA%
if exist benchmarks\data\scifact.json %B% retrieval --dataset benchmarks\data\scifact.json --ingest-concurrency 8 %EXTRA%
if exist benchmarks\data\longmemeval-s-60.json %B% retrieval --dataset benchmarks\data\longmemeval-s-60.json --modes Semantic,Hybrid --ingest-concurrency 8 %EXTRA%

set TEMPLATES=
if exist benchmarks\data\scifact.json set TEMPLATES=--dataset benchmarks\data\scifact.json
%B% load --stub --stub-latency-ms 5 --corpus-size 10000 --concurrency 1,4,16,64 --duration 30 --warmup 5 %TEMPLATES%

%B% chat --dataset benchmarks\datasets\isis-live.json

if "%~1"=="--agent" %B% agent --tasks benchmarks\agent\tasks-isis.json --model %AGENT_MODEL%

popd
endlocal
