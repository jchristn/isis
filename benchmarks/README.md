# Isis benchmarks

This directory holds a reproducible benchmark suite for Isis, the agent-memory platform in this repository. It
answers three questions: does Isis retrieve the right memories, do answers grounded on those memories come out
right, and does an agent actually do better with Isis connected? It also measures what that costs in latency and
throughput. [RESULTS.md](RESULTS.md) has the current numbers and how they changed across rounds of fixes.

The harness (`src/Test.Benchmark`) is black-box. It talks to Isis only over REST and MCP, the same way a client does,
and never references Isis assemblies. The same commands can therefore measure a local working tree or any
deployment you point them at.

| Command | Measures |
|---|---|
| `retrieval` | Search accuracy (Hit@1, Recall@1/5/10, All@5/10, MRR@10, nDCG@10) per search mode and query type, plus latency, a server-side stage breakdown, and how well scores separate answerable from unanswerable questions |
| `chat` | End-to-end chat-with-memory: LLM-judged answer accuracy, abstention on unanswerable questions, citation precision and recall, and whether retrieval put the evidence in the prompt at all |
| `agent` | Claude Code run headless on memory-dependent tasks, once with the Isis MCP server connected and once with no memory (success rate, turns, cost) |
| `load` | Closed-loop throughput and latency percentiles per concurrency level for a search/upsert mix, optionally against a stub embedding server so Isis and RecallDB are measured without the model |
| `compare` | Diffs two retrieval reports and exits non-zero on a regression, so it can gate CI |
| `prepare` | Converts BEIR and LongMemEval downloads into the harness's dataset format |
| `stub` | Runs the stub embedding server on its own |

## 1. Stand up an isolated stack

Benchmarks run against their own pgvector and RecallDB containers on non-default ports, and a local Isis built from
the working tree. They never touch the development stack in `docker/` or a live deployment.

```bash
docker compose -f benchmarks/docker/compose.yaml up -d      # pgvector :15432, RecallDB :18600
dotnet build src/Isis.sln -c Release
benchmarks/start-bench-server.sh                            # Isis REST :18700, Prometheus :19464  (.bat on Windows)
benchmarks/start-bench-mcp.sh                               # Isis MCP  :18720 (agent benchmark only)
ollama pull all-minilm                                      # default embedding model (384-dim)
ollama pull gemma3:4b                                       # default chat model and judge
```

`docker compose -f benchmarks/docker/compose.yaml down -v` tears the stack down and discards all benchmark data.

## 2. Datasets

Every dataset uses one neutral JSON format (`src/Test.Benchmark/Datasets/BenchmarkDataset.cs`). A dataset has one or
more **corpora**, and each corpus becomes its own Isis scope. A corpus holds categories, documents (each becomes a
memory, and its `id` becomes the memory slug), and labelled queries (`text`, `type`, `relevant` document ids, an
optional `category` filter, and an optional gold `answer`). An empty `relevant` list marks a question the corpus
cannot answer, which is how abstention is tested.

| Dataset | In the repo? | Size | What it tests |
|---|---|---|---|
| `datasets/isis-live.json` | yes | 24 memories, 110 questions | The real memories an agent wrote about this project, with questions of six types: paraphrase, lexical (exact identifiers), multi (2–3 memories), category (uses a filter), detail (answer deep in a long memory), and negative (no answer exists) |
| `datasets/atlas.json` | yes | 170 memories, 260 questions | A synthetic agent-memory corpus for a fictional logistics company, adding superseded facts (an old and a newer memory), confusable near-duplicates, and long documents |
| SciFact (BEIR) | downloaded | 5,183 abstracts, 300 queries | Scientific claim retrieval, useful as a sanity check against published numbers for the embedding model |
| LongMemEval-S | downloaded | 60 of 500 questions (stratified) | Multi-session chat memory with seven question types; every question has its own haystack of about 50 sessions |

The two committed datasets were built for this suite. Both question sets were written with LLM assistance and then
checked against the corpus. For Atlas, the questions were written by a separate pass that had not written the corpus,
to keep them from echoing its phrasing. Treat the labels as good but not gold: a handful of questions have more than
one defensible answer, and the per-type breakdowns are more trustworthy than any single question.

The public datasets are downloaded at run time and are not redistributed here. Check their licenses upstream (SciFact
through BEIR, and LongMemEval) before publishing derived data. To download and convert them into the git-ignored
`benchmarks/data/`:

```bash
B="dotnet run --project src/Test.Benchmark -c Release --"
curl -L -o benchmarks/data/scifact.zip https://public.ukp.informatik.tu-darmstadt.de/thakur/BEIR/datasets/scifact.zip && unzip -o benchmarks/data/scifact.zip -d benchmarks/data
curl -L -o benchmarks/data/longmemeval_s_cleaned.json https://huggingface.co/datasets/xiaowu0162/longmemeval-cleaned/resolve/main/longmemeval_s_cleaned.json
$B prepare --format beir --input benchmarks/data/scifact --name scifact --output benchmarks/data/scifact.json
$B prepare --format longmemeval --input benchmarks/data/longmemeval_s_cleaned.json --limit 60 --seed 7 --output benchmarks/data/longmemeval-s-60.json
```

`--limit` takes a stratified sample (round-robin across question types) that is reproducible with `--seed`. The full
LongMemEval-S set is 500 haystacks of about 50 sessions each. With a local all-minilm on a laptop (about 70
embeddings/s) that is roughly 300k embedding calls, so start with the sample.

Documents that carry dates (Atlas and LongMemEval) are written one at a time in date order, so each memory's write
time follows the order its facts were recorded. That matters because Isis uses write time as a recency signal in
hybrid search. Corpora without dates are written in parallel.

## 3. Run

`run-baseline.sh` (or `run-baseline.bat`) runs the standard suite: retrieval on every dataset present, the load test,
and chat. Pass `--agent` to include the agent benchmark, which spends real API credits. Individual commands:

```bash
B="dotnet run --project src/Test.Benchmark -c Release --no-build --"

# Retrieval accuracy (all three modes by default)
$B retrieval --dataset benchmarks/datasets/isis-live.json
$B retrieval --dataset benchmarks/data/scifact.json --ingest-concurrency 8
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --modes Semantic,Hybrid

# Ablations on the same ingested scopes: recency off, or a score threshold
$B retrieval --dataset benchmarks/datasets/atlas.json --modes Hybrid --recency-weight 0 --label recency0
$B retrieval --dataset benchmarks/datasets/isis-live.json --modes Hybrid --min-score 0.3 --label min03

# Round-3 options: superseded handling, link expansion, diversity (all per query)
$B retrieval --dataset benchmarks/datasets/atlas.json --modes Hybrid --superseded Include --label sup-include
$B retrieval --dataset benchmarks/datasets/isis-live.json --modes Hybrid --link-expansion 2 --label link2
$B retrieval --dataset benchmarks/datasets/isis-live.json --modes Hybrid --diversity 0.3 --label div03

# Reranking: start the optional TEI cross-encoder, then --rerank attaches it to every scope in the run (runs without
# --rerank detach it). --min-rerank-score is a per-query cutoff; --scope-min-rerank-score sets the scope's cutoff,
# which is what chat uses.
docker compose -f benchmarks/docker/compose.yaml --profile rerank up -d
$B retrieval --dataset benchmarks/datasets/isis-live.json --modes Hybrid --rerank --label rerank
$B chat --dataset benchmarks/datasets/isis-live.json --rerank --scope-min-rerank-score 0.02 --label rerank-cut

# Chunking sweep: a suffix keeps the variants in separate scopes
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --chunk-overlap 0   --scope-suffix ov0
$B retrieval --dataset benchmarks/data/longmemeval-s-60.json --chunk-overlap 128 --scope-suffix ov128

# Chat-with-memory, judged by a model called directly (never through Isis). Uses the server's default retrieval
# depth unless --k is given. Use a non-reasoning model: Isis does not disable thinking, so a reasoning model spends
# minutes per answer on a laptop GPU.
$B chat --dataset benchmarks/datasets/isis-live.json --inference-model gemma3:4b --judge-model gemma3:4b

# Agent-in-the-loop (spends real API credits; uses the claude CLI's own authentication)
$B agent --tasks benchmarks/agent/tasks-isis.json --model haiku

# Load: stub embeddings isolate Isis + RecallDB; the real model shows end-to-end capacity
$B load --stub --stub-latency-ms 5 --corpus-size 10000 --concurrency 1,4,16,64 --duration 30 --dataset benchmarks/data/scifact.json
$B load --corpus-size 1000 --concurrency 1,4,16 --scenario search

# Regression gate
$B compare --baseline benchmarks/results/<old>.json --candidate benchmarks/results/<new>.json --tolerance 0.01 --latency-tolerance 0.25
```

Each run writes `benchmarks/results/<utc-stamp>-<kind>-<name>.json` for machines and a `.md` for people. The
directory is git-ignored because the reports are machine-specific; RESULTS.md carries the numbers that matter. Every
report records the git commit (marked `-dirty` for uncommitted changes), the machine, the embedding and inference
endpoints, and the configuration. Scope names are deterministic (dataset, corpus, embedding model, suffix), so a
rerun reuses an already-ingested scope. Pass `--reingest` after changing chunking or embedding code.

Atlas documents that replace an earlier one carry `supersedes`, which the harness sends on upsert. On a fresh ingest
the report also shows how many of those known pairs the server's similarity check flagged (`similarMemories`).

## 4. How to read the results

A few of the report sections are easy to misread.

**Stage breakdown.** The harness scrapes the Prometheus histograms Isis already exports before and after each
phase, which gives the server-side mean time per stage (`memory_search`, `embedding`, `store_search`, `db_query`, and
so on). Compare that with client latency to see where the time actually goes.

**Score separation.** The report compares the mean top-hit score of answerable and unanswerable questions. When the
two are close, no score threshold can say "nothing relevant", however it is tuned. Hybrid scores are fused
reciprocal-rank scores normalized to 0..1, so they are comparable across queries in a way raw similarities are not.
On a reranked run the top score is the reranker's score. The table also shows how often a cutoff returned no hits
for answerable questions (lost answers) and for unanswerable ones (correct "nothing relevant").

**Evidence retrieved vs. missed (chat).** Accuracy is reported separately for questions where retrieval did and did
not put the evidence in the prompt. That split tells retrieval failures apart from generation failures.

**Agent arms.** The `none` arm is the floor: what the model answers without memory. The gap to the `isis` arm is what
memory adds. Each run uses an empty temporary directory with every built-in tool disabled, so project facts can only
come from Isis.

**Judges.** Chat accuracy is graded by an LLM judge called directly, never through Isis. The default is the same small
local model that answers, which is cheap but noisy; expect a few points of variance between runs. For headline
numbers, point `--judge-*` at a stronger model and spot-check around 50 verdicts by hand.

**Hardware.** Absolute latencies and throughput depend on the machine. RESULTS.md records what it ran on. Compare
runs from the same machine, and use `compare` for regressions rather than absolute thresholds.
