# Isis benchmark results: first baseline (2026-09-23/24)

**Setup:**

- Machine: AMD Ryzen AI 9 HX PRO 370 (12 cores), Radeon 890M iGPU, Windows 11.
- Isis: built from the working tree against the isolated bench stack (`benchmarks/docker`: pgvector 0.5.1 / PostgreSQL 15.4, RecallDB 0.2.1).
- Embeddings: local Ollama `all-minilm` (384-dim).
- Chat: `gemma3:4b` as the answer model, and also as the judge (a small judge, so treat chat numbers as ±5 points).

Raw per-run reports (`.json` and `.md`) are written to `benchmarks/results/`, which is git-ignored because it is machine-specific. The numbers below are copied from those reports.

## Retrieval accuracy (after fixes)

| Dataset | Queries | Mode | Hit@1 | Recall@5 | MRR@10 | nDCG@10 | p50 ms |
|---|---|---|---|---|---|---|---|
| isis-live (24 real memories) | 90 answerable + 20 negative | Keyword | 0.078 | 0.072 | 0.078 | 0.073 | 26 |
| | | Semantic | 0.700 | 0.941 | 0.808 | 0.844 | 45 |
| | | Hybrid | 0.711 | 0.941 | 0.814 | 0.848 | 56 |
| Atlas (170 synthetic memories) | 220 + 40 negative | Keyword | n/a | 0.136 | 0.147 | 0.137 | 81 |
| | | Semantic | n/a | 0.830 | 0.693 | 0.715 | 32 |
| | | Hybrid | n/a | 0.837 | 0.722 | 0.739 | 33 |
| SciFact (BEIR, 5,183 docs) | 300 | Keyword | 0.060 | 0.056 | 0.060 | 0.057 | 22 |
| | | Semantic | 0.503 | 0.732 | 0.608 | **0.653** | 46 |
| | | Hybrid | 0.523 | 0.739 | 0.622 | 0.665 | 53 |
| LongMemEval-S (60 of 500, stratified) | 60 | Keyword | n/a | 0.098 | 0.118 | 0.102 | 14 |
| | | Semantic | n/a | 0.947 | 0.828 | 0.853 | 34 |
| | | Hybrid | n/a | 0.947 | 0.828 | 0.853 | 43 |

- **Sanity check:** the published all-MiniLM-L6-v2 SciFact nDCG@10 is 0.645. Isis's Semantic score of 0.653
  shows the pipeline (chunking, storage, retrieval) loses nothing relative to the raw model.
- **Keyword is broken upstream.** RecallDB ANDs every query term (`plainto_tsquery`). Simulating OR semantics on
  isis-live gives Keyword recall@5 **0.859**, and moves Hybrid nDCG@10 from 0.848 to **0.867**. The fix plan is in
  `C:\Code\RecallDB\RecallDB\HYBRID_SEARCH_FIX.md`.
- **Weak spots, by query type:**
  - Atlas paraphrase: Hit@1 0.33 (a limit of the embedding model).
  - Atlas superseded facts: Hit@1 0.40. The old version of a fact often outranks its replacement, because Isis has
    no recency or supersession signal. This is the planned "living-memory hygiene" work.
  - Atlas multi-memory questions: all needed memories appear in the top 10 only 47% of the time.
  - LongMemEval preference questions (Hit@1 0.38) and temporal questions (nDCG 0.71).
- **No abstention signal.** The mean top score for unanswerable questions is close to the mean for answerable ones
  (0.36 vs 0.42 on isis-live, 0.47 vs 0.51 on Atlas), so no score threshold can say "nothing relevant".

## After the RecallDB full-text patch (2026-09-24)

These runs used RecallDB images rebuilt with the `HYBRID_SEARCH_FIX.md` changes, still tagged `v0.2.1`, against the
same ingested scopes. Isis is unchanged: it still sends a text-only query for Keyword and fuses two separate searches
itself for Hybrid. The whole improvement comes from RecallDB's new defaults.

On first startup, RecallDB's migration added `content_tsv` and `_tsv` indexes to all 65 collection tables (about 50k
documents) in roughly a minute and dropped every old `_fts` index. RecallDB did not accept requests until the
migration finished, and it logs no per-collection progress.

| Dataset | Mode | Recall@5 before → after | nDCG@10 before → after | p50 ms before → after |
|---|---|---|---|---|
| isis-live | Keyword | 0.072 → **0.856** | 0.073 → **0.817** | 26 → 29 |
| | Hybrid | 0.941 → **0.957** | 0.848 → **0.859** | 56 → 40 |
| Atlas | Keyword | 0.136 → **0.832** | 0.137 → **0.767** | 81 → 24 |
| | Hybrid | 0.837 → **0.877** | 0.739 → **0.803** | 33 → 55 |
| SciFact | Keyword | 0.056 → **0.671** | 0.057 → **0.598** | 22 → 73 (p95 162) |
| | Hybrid | 0.739 → **0.757** | 0.665 → **0.688** | 53 → 106 (p95 239) |
| LongMemEval-S (60) | Keyword | 0.098 → **0.848** | 0.102 → **0.833** | 14 → 37 |
| | Hybrid | 0.947 → **0.975** | 0.853 → **0.906** | 43 → 45 |

Semantic is unchanged, as expected (isis-live 0.844, Atlas 0.714, SciFact 0.653, LongMemEval 0.853 nDCG@10).

Against the plan's acceptance targets, isis-live Keyword recall@5 is 0.856 (target ≥ 0.80), SciFact Keyword nDCG@10
is 0.598 (target ≥ 0.50), and Hybrid beats Semantic on every dataset.

The LongMemEval rerun re-ingested 14 of its 60 haystacks, whose earlier runs had lost sessions to the socket and
tokenizer bugs. They now ingest with 0 failures, so its "after" numbers also include those previously missing
sessions.

On SciFact, Keyword got slower because it now does real work. An OR query of six scientific terms matches 26% of
the 11k chunk documents (2,863 rows). `EXPLAIN ANALYZE` shows the GIN `_tsv` index is used and the rank-and-sort
takes about 21 ms in Postgres. The rest of the p50 is RecallDB overhead (including its count query over the same
match set) and HTTP. Isis's Hybrid pays for both legs concurrently.

There are two ways to win the latency back:

- **In Isis:** replace the two-call client-side fusion with one server-side `Hybrid.Strategy = Rrf` call. This needs
  a RecallDB SDK that exposes `Hybrid` (see §11 of the plan).
- **In RecallDB:** skip or cap the `COUNT(*)` for ranked text queries.

## Chat-with-memory (isis-live, 110 questions)

| | Before (240-char snippets) | After (whole best chunk) |
|---|---|---|
| Answer accuracy (answerable) | 0.678 | **0.956** |
| Accuracy when the evidence was retrieved | 0.706 | 0.976 |
| Detail / paraphrase questions | 0.53 / 0.54 | 0.93 / 0.97 |
| Abstention on unanswerable questions | 0.95 | 0.90 |
| Citation precision / recall | 0.52 / 0.72 | 0.65 / 0.85 |
| Context recall (evidence in the prompt) | 0.918 | 0.918 |
| Latency p50 (local 4B model on iGPU) | 8.2 s | 14.9 s (larger prompts) |

## Load (stub embeddings at 5 ms, 10k memories, 90% search / 10% upsert, 30 s per level)

This is a same-conditions A/B: HEAD built in a git worktree against the same database and scope, 2 runs each.

| Concurrency | Throughput HEAD → new | Search p50 HEAD → new | Upsert p50 HEAD → new | Errors HEAD → new |
|---|---|---|---|---|
| 1 | 19 → 23 ops/s | 47 → 33 ms | 79–95 → 85–88 ms | 0 → 0 |
| 4 | 64–71 → 77–83 | 50–56 → 40–44 | 106–119 → 110–118 | ≤0.1% → 0 |
| 16 | 119–128 → 139 | 110–120 → 93 | 232–242 → 288 | ≤0.1% → 0 |
| 64 | 142 → 139–142 | 381–392 → 336–344 | 940–1030 → 1415–1440 | 0.2% → 0 |

- **RecallDB is the ceiling**, at about 140 ops/s on this machine. At c=64, store search is ~310 ms of a ~335 ms
  search.
- **At saturation, searches win.** Parallel hybrid legs give searches a larger share of RecallDB, so upserts queue
  longer when the mix is 90% search. This is a scheduling trade-off, not an upsert-path regression: c=1 upserts are
  unchanged.
- **Ingest with the real model** (Ollama all-minilm on iGPU) is about 18–24 docs/s at concurrency 8. The embedding
  model is the bottleneck: ~70 embeddings/s. Ollama supports a batch `/api/embed` that Isis doesn't use yet.

## Agent-in-the-loop (Claude Code 2.1.281, haiku, 24 tasks over the isis-live memories)

Each task ran twice: once with the Isis MCP server connected, and once with no memory. Every run used an empty
working directory with all built-in tools disabled, so project facts could only come from Isis. Grading is by regex
on the final answer. Before Voltaic 1.1.0 this benchmark could not run at all, because Claude Code saw no Isis
tools.

| Arm | Success | Mean turns | Mean cost | Total cost | Mean duration |
|---|---|---|---|---|---|
| With Isis | **96%** (23/24) | 2.83 | $0.021 | $0.51 | 10.6 s |
| No memory | 21% (5/24) | 1.00 | $0.015 | $0.37 | 6.5 s |

The no-memory arm's five passes are generic-knowledge or abstention tasks (t10, t13, t20, t23, t24), which is the
expected floor. The one Isis-arm miss (t08) answered with the MCP tool name (`instructions` with a `scopeId`) rather
than the REST route the task asked for (`effective-instructions`). The answer is defensible for an MCP caller, so
the task's expectation could reasonably accept either.

Memory costs about one extra agent turn (2.8 vs 1.0), four seconds, and $0.006 per task, and it takes success from
21% to 96%.

## Defects found and fixed

| # | Defect | Found by | Fix |
|---|---|---|---|
| 1 | MCP `memory_search` category filter by name matched nothing (store labels are ids) | live probe | Filter accepts a name or `cat_` id; unknown category → 400 |
| 2 | Empty `queryText` returned top-K hits with score 0 | live probe | 400 |
| 3 | Concurrent first writes to a new scope raced to provision the RecallDB tenant/collection | retrieval ingest (23/24 failed) | Per-scope provisioning lock; waiters adopt the winner's collection; tenant create tolerates a concurrent create |
| 4 | Chunks exceeded all-minilm's context on technical and accented text (Ollama counts more tokens than the HF WordPiece vocabulary) | SciFact (19% failed), LongMemEval (Turkish) | 4% chunk margin, plus re-chunking at 0.75/0.5/0.3 of the budget on context-length rejection |
| 5 | Concurrent upserts of the same memory raced (duplicate keys, 500s) | load test | Per-memory keyed lock |
| 6 | Exceptions a route didn't catch returned Watson's HTML 500 page | load test | Global `Routes.Exception` handler returning JSON (400/501/503/500) |
| 7 | New `RecallDbClient` (and `HttpClient`) per request, never disposed | code map; socket exhaustion during LongMemEval | Shared client per endpoint |
| 8 | Chat grounded on 240-character snippets | chat benchmark | Grounds on the whole best chunk (UI preview stays short) |

Performance changes: hybrid legs run in parallel, and a multi-chunk memory's chunks are embedded concurrently (4 at
a time).

## Open issues (not fixed here)

- **Resolved (Voltaic 1.1.0): Claude Code 2.1.x could not use Isis's MCP tools.** Isis is now on 1.1.0, and McpSuite has regression cases `stateless-claude-sequence` and `initialize-caps-stateless-version`. The notes below describe the original diagnosis. The actual Claude Code flow is `server/discover` followed by stateless requests, which also need `ttlMs`/`cacheScope`.
  - Isis now uses Voltaic 0.7.1 (bumped 2026-09-24). The problem is identical on 0.7.1 and 0.6.1.
  - Voltaic negotiates protocol revision `2026-07-28` whenever the client requests it.
  - Its session-mode `tools/list` then omits the `resultType` field that revision requires, so the client rejects the
    tool list.
  - The live deployment negotiates the same revision.
  - The fix belongs in Voltaic (cap negotiation on the session path, or emit `resultType`). Isis cannot cap the
    version: `McpHttpServer.ProtocolVersion` only sets the default for clients that don't ask.
  - The agent benchmark (`agent` command, `agent/tasks-isis.json`) is built but blocked on this.
- RecallDB keyword search: fixed upstream and verified (see "After the RecallDB full-text patch"). Remaining: SciFact-scale text-query latency, and moving Isis to server-side RRF once the SDK exposes `Hybrid`.
- No relevance threshold or abstention in search; no recency or supersession awareness.
- Auth does 2 DB reads per request (credential and user). A short-TTL cache would save ~5 ms per call, at the cost
  of revocation latency. This is a policy decision.
- Upserts embed one text per HTTP call. Batch embedding APIs (Ollama `/api/embed`, OpenAI list input) would raise
  ingest throughput.
- One full test-suite run hung once and did not reproduce in 3 reruns. It may have been a Docker-backed live-DB
  test.
