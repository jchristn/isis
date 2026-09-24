# Isis benchmark results

This page records what the benchmark suite in this directory measured, round by round, and what changed in Isis
between rounds. The short version: with Isis connected, Claude Code completed 96% of memory-dependent tasks against
17% without it; chat answers grounded on Isis memories are right 99% of the time on the isis-live questions; and
hybrid retrieval now reaches nDCG@10 of 0.80 to 0.91 on the three memory-style datasets. The weak spots are
paraphrased questions, questions that need several memories at once, and knowing when nothing relevant exists.

Every number here comes from the harness described in [README.md](README.md). Raw per-run reports are written to
`benchmarks/results/`, which is git-ignored because it is machine-specific.

## Setup

All rounds ran on one laptop: an AMD Ryzen AI 9 HX PRO 370 (12 cores) with a Radeon 890M integrated GPU, on Windows
11. The stack was isolated: pgvector 0.5.1 on PostgreSQL 15.4, RecallDB 0.2.1, and Isis built from the working tree.
Embeddings came from a local Ollama `all-minilm` (384 dimensions). Chat answers came from `gemma3:4b`, which also
served as the judge. A 4B judge is noisy, so read chat accuracy as plus or minus a few points. The agent benchmark
used Claude Code 2.1.281 with `haiku`.

Latency and throughput depend on the machine. Compare them within this page, not against other hardware.

## The rounds

| Round | What changed |
|---|---|
| 0 | First baseline, run against the code as it was when the harness was written |
| 1 | Nine Isis defects fixed (see [Defects found](#defects-found-by-the-benchmarks)); chat grounds on whole chunks instead of 240-character snippets; Voltaic 1.1.0 so Claude Code can see Isis tools; RecallDB patched upstream so keyword search matches any term instead of requiring every term |
| 2 | The retrieval improvements in [RETRIEVAL_IMPROVEMENTS.md](../RETRIEVAL_IMPROVEMENTS.md): fused and normalized hybrid scores, a recency signal, chunk headers, deeper chat retrieval, a stricter chat prompt, and update-not-duplicate guidance, plus four ingest robustness fixes found while measuring |

## Retrieval

nDCG@10 scores whether the right memories come back near the top, from 0 to 1. Hybrid is the default mode and the
one agents and chat use.

| Dataset | Mode | Round 0 | Round 1 | Round 2 |
|---|---|---|---|---|
| isis-live: 24 real memories, 110 questions | Keyword | 0.073 | 0.817 | 0.814 |
| | Semantic | 0.844 | 0.844 | 0.843 |
| | **Hybrid** | 0.848 | 0.859 | **0.877** |
| Atlas: 170 synthetic memories, 260 questions | Keyword | 0.137 | 0.767 | 0.773 |
| | Semantic | 0.715 | 0.714 | 0.738 |
| | **Hybrid** | 0.739 | 0.803 | **0.804** |
| SciFact: 5,183 abstracts, 300 queries | Keyword | 0.057 | 0.598 | 0.585 |
| | Semantic | 0.653 | 0.653 | 0.645 |
| | **Hybrid** | 0.665 | 0.688 | **0.678** |
| LongMemEval-S: 60 haystacks of about 50 sessions | Keyword | 0.102 | 0.833 | 0.838 |
| | Semantic | 0.853 | 0.853 | 0.887 |
| | **Hybrid** | 0.853 | 0.906 | **0.907** |

Round 0's retrieval numbers were measured once the ingest-blocking bugs of that round were fixed, so every document
was actually stored, but before the RecallDB keyword fix and the chat changes. The very first SciFact run lost 19% of
its documents to a token-budget bug, and it reported nDCG 0.52 until that was fixed.

SciFact is the sanity check. Its semantic score (0.645 to 0.653) sits on the published figure for
all-MiniLM-L6-v2 (0.645), so chunking and storage cost nothing. The jump in round 1 is almost all keyword search,
which was essentially broken until RecallDB stopped requiring every query term to appear.

Round 2's gains are narrower than round 1's, and they land where they were aimed:

| Atlas, Hybrid, by question type | Hit@1 round 1 → 2 | nDCG@10 round 1 → 2 |
|---|---|---|
| superseded (a fact and its later replacement) | 0.40 → **0.55** | 0.718 → **0.777** |
| paraphrase | 0.43 → 0.45 | 0.673 → **0.707** |
| multi (needs 2 or 3 memories) | 0.80 → 0.83 | 0.785 → 0.799 |
| confusable near-duplicates | 0.72 → 0.68 | 0.852 → 0.859 |
| detail deep in a long memory | 0.90 → 0.87 | 0.947 → 0.933 |
| category-filtered | 0.95 → 0.95 | 0.975 → 0.982 |
| **lexical (exact identifiers)** | 0.77 → **0.57** | 0.833 → **0.740** |

The lexical drop is the one real regression, and it comes from chunk headers. Embedding each chunk with its
memory's title and summary helps meaning-based questions but blurs the vector leg's ranking of memories that differ
mainly by identifier. Hybrid still has the keyword leg for those, but less decisively than before. Revisiting how
the header is applied is on the list of next steps.

SciFact slipped by a point in round 2 for a reason that does not apply to agent memory: it is a bulk import with no
meaningful write order, so the new recency signal is noise there. With recency off, SciFact scores 0.693, above round
1. Scopes loaded by bulk import should set `recencyWeight` to 0.

### Choosing the recency weight

The recency weight was chosen by sweeping it on all four datasets, reusing the same ingested scopes.

| Hybrid nDCG@10 | w = 0 | 0.03 | 0.05 | **0.1 (default)** | 0.2 |
|---|---|---|---|---|---|
| Atlas overall | 0.806 | 0.805 | 0.803 | 0.804 | 0.803 |
| Atlas, superseded facts only | 0.703 | 0.722 | 0.722 | **0.777** | 0.814 |
| LongMemEval | 0.906 | 0.909 | 0.909 | 0.909 | 0.911 |
| isis-live (undated) | 0.872 | 0.880 | 0.876 | 0.873 | 0.856 |
| SciFact (bulk import) | 0.693 | 0.685 | 0.682 | 0.680 | 0.658 |

Superseded facts keep improving as the weight rises. Undated and bulk-imported corpora start to pay for it above
0.1. At 0.1, Isis gets most of the superseded-fact gain for about one point of nDCG on the bulk import.

### Can a score say "nothing relevant"?

Each dataset includes questions the memories cannot answer. The harness measures how well the top hit's score
separates answerable from unanswerable questions, as AUROC: 0.5 is a coin flip, 1.0 is a perfect threshold.

| AUROC, Hybrid mode | isis-live | Atlas | LongMemEval |
|---|---|---|---|
| Fused hybrid score | 0.64 | 0.67 | 0.57 |
| Raw vector similarity (`vectorScore`) | 0.67 | 0.64 | 0.78 |

Neither is reliable enough to act as an abstention gate. The fused score is a rank-fusion score, so every query's
best hit lands near 1.0 whether or not it is relevant. The raw vector similarity, which Isis now returns on every
hit, is the better signal, and it is usable as a soft filter on LongMemEval. A reranker with calibrated scores is the
fix that would change this picture.

### Latency

Hybrid search p50 in round 2 was 42 ms on isis-live and Atlas, 38 ms on LongMemEval, and 95 ms on SciFact. SciFact is
slower because any-term text matching over 11,000 chunk documents ranks thousands of matches per query; the full-text
index is used, and the cost is the ranking itself. Round 2 did not change the search path's cost.

## Chat with memory

The isis-live questions, asked through the Isis chat route and graded by an independent model call:

| | Round 0 | Round 1 | Round 2 |
|---|---|---|---|
| Answer accuracy (90 answerable questions) | 0.678 | 0.956 | **0.989** |
| Evidence reached the prompt | 0.918 | 0.918 | **0.974** |
| Accuracy when the evidence was in the prompt | 0.706 | 0.976 | 0.989 |
| Correctly declined (20 unanswerable questions) | 0.95 | 0.90 | 0.85 |
| Citation precision / recall | 0.52 / 0.72 | 0.65 / 0.85 | 0.66 / 0.87 |
| Latency p50 | 8.2 s | 14.9 s | 15.4 s |

Round 1's jump came from one change: chat used to ground the model on a 240-character preview of each memory, so any
answer past a memory's first sentence was invisible. Round 2's gain came from retrieving 8 memories instead of 5,
which put the evidence in the prompt for 97% of questions instead of 92%.

Declining unanswerable questions got slightly worse in each round, 19, then 18, then 17 of 20, even though round 2's
prompt tells the model to say when the answer is not in memory. A larger, more complete context gives a small model
more near-relevant material to talk itself into an answer with. The latency increase is the larger prompt on a laptop
GPU.

## Agents over MCP

Claude Code ran 24 tasks whose answers live in the isis-live memories, once with the Isis MCP server connected and once
with no memory. Each run used an empty directory with all built-in tools disabled, so project facts could only come
from Isis.

| Arm | Round 1 | Round 2 | Mean turns | Mean cost per task |
|---|---|---|---|---|
| With Isis | 96% (23/24) | **96%** (23/24) | 3.2 | $0.024 |
| No memory | 21% (5/24) | 17% (4/24) | 1.0 | $0.015 |

Before round 1 this benchmark could not run at all. Voltaic 0.6 and 0.7 left out a field the MCP 2026-07-28 revision
requires, so Claude Code connected to Isis and saw zero tools. The no-memory arm's passes are general-knowledge and
abstention tasks, which is the expected floor. The single Isis miss answered a "which REST route" question with the
equivalent MCP tool name.

## Load

The load test ran in round 1, as a same-conditions A/B against the pre-fix build. Both builds used stub embeddings, so
it measures Isis and RecallDB without the model. The corpus was 10,000 memories with a 90/10 search and upsert mix.

| Concurrency | Throughput before → after | Search p50 before → after | Error rate before → after |
|---|---|---|---|
| 1 | 19 → 23 ops/s | 47 → 33 ms | 0 → 0 |
| 16 | 119–128 → 139 ops/s | 110–120 → 93 ms | up to 0.1% → 0 |
| 64 | 142 → 139–142 ops/s | 381–392 → 336–344 ms | 0.2% → 0 |

RecallDB is the ceiling at about 140 operations per second on this machine. With the real embedding model, ingest is
bound by the model instead, at about 70 embeddings per second through Ollama on the integrated GPU.

## Defects found by the benchmarks

The benchmarks paid for themselves mostly by finding bugs. Every item below was found by a run, fixed, and covered by a
test.

| Found in | Defect | Symptom |
|---|---|---|
| Round 0 | MCP `memory_search` category filter compared a name to stored ids | Filtering by name matched nothing |
| Round 0 | Empty search text was accepted | Top-k hits with score 0 instead of a 400 |
| Round 0 | Concurrent first writes to a new scope raced to create its collection | 23 of 24 ingests failed |
| Round 0 | Chunks sized to the model's exact token limit | 19% of SciFact ingests rejected, because Ollama counts technical text differently from the local tokenizer |
| Round 0 | Concurrent upserts of the same memory raced | Duplicate-key errors under load |
| Round 0 | Exceptions a route didn't catch returned the web server's HTML error page | Clients received an unparseable 500 |
| Round 0 | A new RecallDB HTTP client for every request, never disposed | Socket exhaustion during long ingests |
| Round 0 | Chat grounded on 240-character snippets | Accuracy 0.68 |
| Round 0 | Voltaic MCP responses lacked a field required by the 2026-07-28 revision | Claude Code saw no Isis tools |
| Round 2 | Collection creation for scopes provisioned in parallel collided in RecallDB, and Isis never recovered | 183 of 2,891 LongMemEval ingests failed |
| Round 2 | A stray invalid Unicode code unit | The whole memory could not be stored |
| Round 2 | The chunker could split an emoji in half | The whole memory could not be stored |
| Round 2 | The chunker emitted runs of tiny duplicate tail chunks | 9% of stored chunks on chat sessions were redundant |

Three of these have root causes in libraries Isis depends on, and are worked around in Isis for now:

- **RecallDB:** its per-collection index names use only the time component of the collection id, so collections
  created in the same instant collide.
- **TextChunker (two bugs):** it splits text inside surrogate pairs, and fixed-token chunking with overlap produces
  redundant trailing chunks.

## Environment notes

Heavy ingest runs pushed the machine close to its limit of ephemeral ports. Most of the sockets in `TIME_WAIT`
belonged to Ollama's connections to its own model runner and to the RecallDB and Isis servers closing HTTP
connections after each response, not to leaked Isis clients. It cost one SciFact document in each of the last two
rounds. One LongMemEval upsert in the round-2 re-run exceeded the harness's 10-minute timeout during the same kind of
load, and it did not recur on the final pass (0 of 2,891 failed).

## What's next

[RETRIEVAL_IMPROVEMENTS.md](../RETRIEVAL_IMPROVEMENTS.md) lists every fix considered, scored for value and simplicity,
with what landed in round 2. The results above point at three next steps:

- **Explicit supersession** ("this memory replaces that one"). Recency took superseded facts from 40% to 55% ranked
  first, and the sweep shows a weight cannot push further without hurting other corpora.
- **A stronger embedding model and a reranker.** These address paraphrase, the weakest question type at 45% ranked
  first on Atlas, and the missing "nothing relevant" signal.
- **Revisiting the chunk header**, to win back the lexical-query regression.
