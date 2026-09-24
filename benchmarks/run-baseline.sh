#!/usr/bin/env bash
# Run the standard Isis benchmark suite against the bench stack (see README.md for setup).
#   benchmarks/run-baseline.sh            retrieval + load + chat (no API spend)
#   benchmarks/run-baseline.sh --agent    also run the agent benchmark (spends Claude API credits)
# Results land in benchmarks/results/. Scopes are reused between runs; export REINGEST=1 to rebuild them.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here/.."
B="dotnet run --project src/Test.Benchmark -c Release --no-build --"
extra=()
if [[ "${REINGEST:-0}" == "1" ]]; then extra+=(--reingest); fi

$B retrieval --dataset benchmarks/datasets/isis-live.json "${extra[@]}"
if [[ -f benchmarks/datasets/atlas.json ]]; then $B retrieval --dataset benchmarks/datasets/atlas.json "${extra[@]}"; fi
if [[ -f benchmarks/data/scifact.json ]]; then $B retrieval --dataset benchmarks/data/scifact.json --ingest-concurrency 8 "${extra[@]}"; fi
if [[ -f benchmarks/data/longmemeval-s-60.json ]]; then $B retrieval --dataset benchmarks/data/longmemeval-s-60.json --modes Semantic,Hybrid --ingest-concurrency 8 "${extra[@]}"; fi

$B load --stub --stub-latency-ms 5 --corpus-size 10000 --concurrency 1,4,16,64 --duration 30 --warmup 5 \
  ${SCIFACT:+--dataset benchmarks/data/scifact.json}

$B chat --dataset benchmarks/datasets/isis-live.json --k 5

if [[ "${1:-}" == "--agent" ]]; then
  $B agent --tasks benchmarks/agent/tasks-isis.json --model "${AGENT_MODEL:-haiku}"
fi
