# Isis benchmarks

A black-box benchmark harness (`src/Test.Benchmark`) that measures Isis the way a client sees it, over REST and MCP.
It never references Isis assemblies, so it can target a local working tree or any deployment. It covers four layers:

| Command | Measures |
|---|---|
| `retrieval` | Search accuracy (Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10) per search mode and query type, plus latency and a server-side stage breakdown |
| `chat` | End-to-end chat-with-memory: LLM-judged answer accuracy, abstention on unanswerable questions, citation precision/recall, and whether retrieval put the evidence in the prompt |
| `agent` | Claude Code headless on memory-dependent tasks, with the Isis MCP server connected vs. with no memory (success rate, turns, cost) |
| `load` | Closed-loop throughput and latency percentiles per concurrency level for a search/upsert mix, optionally with a stub embedding server so Isis + RecallDB are measured in isolation |

`compare` diffs two retrieval reports and exits non-zero on a regression, so it can gate CI.

## 1. Stand up an isolated stack

Benchmarks run against their own pgvector + RecallDB on non-default ports, and a local Isis built from the working
tree. They never touch the development stack or a live deployment.

```bash
docker compose -f benchmarks/docker/compose.yaml up -d      # pgvector :15432, RecallDB :18600
dotnet build src/Isis.sln -c Release
benchmarks/start-bench-server.sh                            # Isis REST :18700, Prometheus :19464  (.bat on Windows)
benchmarks/start-bench-mcp.sh                               # Isis MCP  :18720 (agent benchmark only)
ollama pull all-minilm                                      # default embedding model (384-dim)
```

To tear down and discard all benchmark data, run `docker compose -f benchmarks/docker/compose.yaml down -v`.

## 2. Datasets

Every dataset uses one neutral JSON format (`Datasets/BenchmarkDataset.cs`). A dataset has one or more
**corpora**, and each corpus becomes its own Isis scope. A corpus holds:

- **categories**
- **documents**: each becomes a memory, and its `id` becomes the memory slug.
- **queries**: each has `text`, `type`, `relevant` document ids, an optional `category` filter, and an optional `answer`.

An empty `relevant` list marks a question the corpus cannot answer.

| Dataset | Where | What it tests |
|---|---|---|
| `datasets/isis-live.json` | committed | The 24 real memories from the live Isis scope, with 110 hand-labelled questions |
| `datasets/atlas.json` | committed | A synthetic agent-memory corpus for a fictional logistics codebase (see below) |
| SciFact (BEIR) | download | 5,183 scientific abstracts and 300 queries, for comparing against published embedding numbers |
| LongMemEval-S | download | Multi-session chat memory with 7 question types; each question has its own ~50-session haystack |

The isis-live question types are:

- **paraphrase**: no lexical overlap with the memory
- **lexical**: exact identifiers
- **multi**: 2–3 relevant memories
- **category**: uses a category filter
- **detail**: the answer is buried deep in a long memory
- **negative**: the corpus has no answer

Atlas covers all of these, plus superseded facts and near-duplicates.

To download and convert the public sets (both go into the git-ignored `benchmarks/data/`):

```bash
B="dotnet run --project src/Test.Benchmark -c Release --"
curl -L -o benchmarks/data/scifact.zip https://public.ukp.informatik.tu-darmstadt.de/thakur/BEIR/datasets/scifact.zip && unzip -o benchmarks/data/scifact.zip -d benchmarks/data
curl -L -o benchmarks/data/longmemeval_s_cleaned.json https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned/resolve/main/longmemeval_s_cleaned.json
$B prepare --format beir --input benchmarks/data/scifact --name scifact --output benchmarks/data/scifact.json
$B prepare --format longmemeval --input benchmarks/data/longmemeval_s_cleaned.json --limit 60 --seed 7 --output benchmarks/data/longmemeval-s-60.json
```

`--limit` takes a stratified sample, round-robin across question types, and is reproducible with `--seed`. A full
LongMemEval-S run is 500 haystacks of about 50 sessions. With a CPU-bound local all-minilm (about 70 embeddings/s),
that is about 25k sessions and 300k embedding calls, so start with a sample.

## 3. Run

```bash
B="dotnet run --project src/Test.Benchmark -c Release --no-build --"

# Retrieval accuracy (all three modes by default)
$B retrieval --dataset benchmarks/datasets/isis-live.json
$B retrieval --dataset benchmarks/data/scifact.json --ingest-concurrency 8
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --modes Semantic,Hybrid

# Chunking sweep: a suffix keeps the variants in separate scopes
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --chunk-overlap 0   --scope-suffix ov0
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --chunk-overlap 128 --scope-suffix ov128

# Chat-with-memory, judged by a model called directly (never through Isis)
#   (use a non-reasoning model: Isis's chat call does not disable thinking, so a reasoning model such as
#    qwen3.5 spends minutes per answer on a CPU/iGPU. gemma3:4b is the default and matches the live seed.)
$B chat --dataset benchmarks/datasets/isis-live.json --inference-model gemma3:4b --judge-model gemma3:4b

# Agent-in-the-loop (spends real API credits; runs with the claude CLI's own auth)
$B agent --tasks benchmarks/agent/tasks-isis.json --model haiku

# Load: stub embeddings isolate Isis + RecallDB; the real model shows end-to-end capacity
$B load --stub --stub-latency-ms 5 --corpus-size 10000 --concurrency 1,4,16,64 --duration 30 --dataset benchmarks/data/scifact.json
$B load --corpus-size 1000 --concurrency 1,4,16 --scenario search

# Regression gate
$B compare --baseline benchmarks/results/<old>.json --candidate benchmarks/results/<new>.json --tolerance 0.01 --latency-tolerance 0.25
```

Each run writes `benchmarks/results/<utc-stamp>-<kind>-<name>.json` for machines and `.md` for people. Every report
records the git commit (marked `-dirty` for uncommitted changes), the machine, the embedding and inference
endpoints, and the configuration. Scope names are deterministic (dataset, corpus, embedding model, suffix), so a
rerun reuses an already-ingested scope. Pass `--reingest` to rebuild it after changing chunking or embedding code.

## How to read the results

- **Stage breakdown.** The Prometheus histograms Isis already exports are scraped before and after each phase, which
  gives the server-side mean time per stage: `memory_search`, `embedding`, `store_search`, `db_query`, and so on.
  Compare it with client latency to see where time goes.
- **Score separation.** This compares the mean top-hit score of answerable questions with that of unanswerable ones.
  If the two are close, no score threshold can tell "nothing relevant" apart from a real hit.
- **Evidence retrieved vs. missed** (chat). This separates retrieval failures from generation failures.
- **Agent arms.** The `none` arm is the floor, meaning what the model can answer without memory. The gap to the
  `isis` arm is the value memory adds. Runs use an empty temp directory with every built-in tool disabled, so the
  agent can only learn project facts through Isis.
- **Judges.** Small local judges are noisy. For headline numbers, point `--judge-*` at a strong model and
  spot-check about 50 verdicts by hand.

## Baseline results

See [RESULTS.md](RESULTS.md) for the first full baseline and the issues it surfaced.
