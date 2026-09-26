# Isis benchmark results

This page records what the benchmark suite in this directory measured, round by round, and what changed in Isis
between rounds. The short version: with Isis connected, Claude Code completed 96% of memory-dependent tasks against
17% without it; chat answers grounded on Isis memories are right 98% of the time on the isis-live questions and it
now declines every unanswerable one; and hybrid retrieval reaches nDCG@10 of 0.81 to 0.91 on the three memory-style
datasets, or 0.89 to 0.93 with a cross-encoder reranker attached. The weak spots are paraphrase without a reranker,
questions that need several memories at once, and a relevance score that can say "nothing relevant".

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
| 3 | Explicit supersession, optional cross-encoder reranking with a relevance cutoff, link expansion (on in chat), result diversity, similar-memory flags on upsert, and a lookup cache. Reranking, diversity, and the cutoff are off unless configured, so the plain "Round 3" column is the default configuration and "+ rerank" is a scope with a reranker attached |
| 4 | TextChunker 0.3.1 (span-based chunking, token counts that match the embedding runtime); Isis's chunking workarounds removed and its token margin cut from 4% to 1%; every dataset re-ingested |
| 5 | Chunks default to 75% of the model budget (capped at 256 tokens); retries on 429/502/503 with 503 reported for an endpoint still at capacity; 10 rerank candidates by default; embedding task prefixes; prompted chat-model reranking. Every model call (all-minilm, nomic-embed-text, gemma3:4b) ran on one GPU host instead of the laptop |
| 6 | Hybrid fusion's RRF constant 60 → 20 after a sweep; embedding model profiles (nomic-embed-text chunks capped at 128 tokens); the cross-encoder seeded and attached to new scopes by the reference stack, with a circuit breaker; multi-query search and query decomposition (measured, off in chat by default); gpt-oss-20b measured as a prompted reranker |
| 7 | Chat accepts the conversation's earlier messages and rewrites a follow-up into a standalone query before retrieval, searching both; a follow-up question dataset |

## Retrieval

nDCG@10 scores whether the right memories come back near the top, from 0 to 1. Hybrid is the default mode and the
one agents and chat use.

| Dataset | Mode | Round 0 | Round 1 | Round 2 | Round 3 | Round 4 | Round 5 | Round 6 | + rerank | + gpt-oss-20b rerank |
|---|---|---|---|---|---|---|---|---|---|---|
| isis-live: 24 real memories, 110 questions | Keyword | 0.073 | 0.817 | 0.814 | 0.814 | 0.807 | 0.812 | | | |
| | Semantic | 0.844 | 0.844 | 0.843 | 0.843 | 0.848 | 0.826 | | | |
| | **Hybrid** | 0.848 | 0.859 | 0.877 | 0.877 | 0.883 | 0.879 | 0.878 | 0.925 | **0.974** |
| Atlas: 170 synthetic memories, 260 questions | Keyword | 0.137 | 0.767 | 0.773 | 0.783 | 0.772 | 0.767 | | | |
| | Semantic | 0.715 | 0.714 | 0.738 | 0.756 | 0.737 | 0.753 | | | |
| | **Hybrid** | 0.739 | 0.803 | 0.804 | 0.809 | 0.802 | 0.831 | **0.835** | 0.880 | **0.915** |
| SciFact: 5,183 abstracts, 300 queries | Keyword | 0.057 | 0.598 | 0.585 | 0.589 | 0.594 | 0.594 | | | |
| | Semantic | 0.653 | 0.653 | 0.645 | 0.647 | 0.657 | 0.658 | | | |
| | **Hybrid** | 0.665 | 0.688 | 0.678 | 0.678 | 0.683 | 0.682 | 0.683 | 0.715 | **0.751** |
| LongMemEval-S: 60 haystacks of about 50 sessions | Keyword | 0.102 | 0.833 | 0.838 | not run | not run | not run | | | |
| | Semantic | 0.853 | 0.853 | 0.887 | 0.887 | 0.889 | 0.874 | | | |
| | **Hybrid** | 0.853 | 0.906 | 0.907 | 0.907 | 0.895 | 0.912 | 0.911 | 0.931 | **0.959** |

Round 6 changed only how the two legs are fused, so Keyword and Semantic were not re-run. SciFact's published
baselines (BEIR BM25 0.665, BM25 with a cross-encoder 0.688, all-MiniLM-L6-v2 dense 0.645) are in every SciFact report
and in the history command's output; Hybrid is +0.018 over BM25 and +0.038 over the dense model it embeds with.

"+ rerank" is the ms-marco-MiniLM-L-6-v2 cross-encoder with 10 candidates (the round-5 default), measured in round 5.
"+ gpt-oss-20b rerank" is the same pipeline with gpt-oss-20b prompted as the reranker, measured in round 6. The round-3 and
round-4 rerank columns (20 candidates) are in the round sections below. Round 5's reranked numbers were measured on
the laptop with the round-5 code; every other round-5 number ran on the GPU host, whose all-minilm results match the
laptop's to within 0.004.

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

Round 3 without a reranker changes only Atlas, where the replacement facts are now declared (`supersedes`), and
Keyword and Semantic move there too because superseded handling applies in every mode. With the reranker, every dataset
improves, and the question types that were weakest improve most:

| Atlas, Hybrid, by question type | nDCG@10 round 2 | Round 3 | Round 3 + rerank | Hit@1 round 2 → round 3 + rerank |
|---|---|---|---|---|
| superseded | 0.777 | 0.802 | **0.875** | 0.55 → **0.75** |
| paraphrase | 0.707 | 0.707 | **0.833** | 0.45 → **0.70** |
| lexical (exact identifiers) | 0.740 | 0.746 | **0.935** | 0.57 → **0.94** |
| multi (needs 2 or 3 memories) | 0.799 | 0.810 | 0.845 | 0.83 → 0.90 |
| confusable near-duplicates | 0.859 | 0.863 | 0.879 | 0.68 → 0.76 |
| detail deep in a long memory | 0.933 | 0.933 | 0.985 | 0.87 → 0.97 |
| category-filtered | 0.982 | 0.982 | 0.982 | 0.95 → 0.95 |

The reranker undoes round 2's lexical regression: the cross-encoder reads the query and the passage together, so an
exact identifier match counts again even when the chunk-header embedding blurred it.

Supersession on its own, measured by switching it off on the same scope (`superseded: Include`), accounts for the
superseded-fact gain without a reranker: 0.777 with it off (exactly round 2), 0.802 with it on, and hit@1 0.55 to 0.60.
It cannot help when an unrelated memory outranks both versions, which is where the reranker takes over.

The optional features that stay off by default, measured on Hybrid:

| Hybrid nDCG@10 | isis-live | Atlas | SciFact | LongMemEval |
|---|---|---|---|---|
| Round 3 default | 0.877 | 0.809 | 0.678 | 0.907 |
| `diversity: 0.3` | 0.877 | 0.798 | 0.670 | **0.918** |
| `linkExpansion: 2` | 0.852 | n/a | n/a | n/a |
| Reranked | **0.925** | **0.894** | **0.700** | 0.912 |

Diversity helps only on LongMemEval, whose haystacks hold many overlapping chat sessions, and costs about a point
elsewhere. Link expansion lowers ranking scores by design: linked memories are inserted after the result that links
to them, pushing lower-ranked relevant hits down the list. It is meant to widen chat context, where it is on.

The similarity check on upsert flagged none of Atlas's 9 declared replacement pairs at the first threshold tried (0.9).
Measured directly, a memory and its replacement score 0.55 to 0.88 with all-minilm, while distinct but related memories
reach 0.89, so no threshold separates them. The default is now 0.85 (3 of 9 pairs flagged, 2 other pairs across 170
memories); the flags are prompts for the writer, not decisions.

### Round 4: TextChunker 0.3.1, and an accidental benefit removed

Round 4 moved to TextChunker 0.3.1 and re-ingested everything. isis-live and SciFact rose slightly; Atlas fell from
0.809 to 0.802 (detail 0.933 → 0.880, category 0.982 → 0.945, multi 0.810 → 0.783) and LongMemEval from 0.907 to
0.895. The cause was not a defect in the new chunker. TextChunker 0.2.2 undercounted tokens, so in round 3 Ollama
rejected a chunk from 53 of Atlas's 80 long memories, and Isis's fallback re-chunked those memories at 75% of the
budget. Round 3 had been storing smaller chunks by accident. 0.3.1 counts correctly, every chunk fit the first time,
and chunks grew from about 190 to about 240 tokens, which diluted details. Re-ingesting Atlas with 190-token chunks
on purpose scored 0.827; 128-token chunks overshot (0.799). That became round 5's default.

### Round 5: retrieval-sized chunks, and what did not help

With chunks at 75% of the model budget (188 tokens for all-minilm, capped at 256 for larger models), Atlas reached
0.831 and LongMemEval 0.912, the best defaults so far, and superseded facts on Atlas reached 0.876 without a reranker:

| Atlas, Hybrid nDCG@10 by type | Round 1 | Round 2 | Round 3 | Round 4 | Round 5 | Round 3 + rerank |
|---|---|---|---|---|---|---|
| superseded | 0.718 | 0.777 | 0.802 | 0.800 | **0.876** | 0.875 |
| paraphrase | 0.673 | 0.707 | 0.707 | 0.726 | 0.726 | **0.833** |
| lexical | 0.833 | 0.740 | 0.746 | 0.754 | 0.779 | **0.935** |
| multi | 0.785 | 0.799 | 0.810 | 0.783 | 0.810 | **0.845** |
| confusable | 0.852 | 0.859 | 0.863 | 0.868 | **0.901** | 0.879 |
| detail | 0.947 | 0.933 | 0.933 | 0.880 | 0.932 | **0.985** |
| category | 0.975 | 0.982 | 0.982 | 0.945 | 0.982 | 0.982 |

Three candidates were measured and not adopted:

| Hybrid nDCG@10 | Default (all-minilm) | nomic-embed-text | + ms-marco rerank | + bge-reranker-base | + gemma3:4b as reranker |
|---|---|---|---|---|---|
| isis-live | 0.879 | 0.868 | **0.925** | 0.877 | 0.760 |
| Atlas | 0.831 | 0.813 | **0.880** | 0.859 | 0.631 |
| SciFact | 0.682 | 0.683 | **0.715** | 0.685 | 0.568 |
| LongMemEval | 0.912 | 0.866 | **0.931** | 0.920 | 0.764 |

- **nomic-embed-text** (with its task prefixes) is the better model for vector search alone (SciFact Semantic 0.658 →
  0.693) but not in Hybrid, which is what agents and chat use: the keyword leg already recovers most of what it adds,
  and it doubles vector size. all-minilm stays the default.
- **bge-reranker-base** ranks worse than ms-marco-MiniLM everywhere at 4 to 6 times the CPU latency, and its score is no
  better at separating unanswerable questions (AUROC 0.61 to 0.67).
- **gemma3:4b prompted as a reranker** lowers every dataset: its 0 to 10 ratings are coarse and miss paraphrases a
  cross-encoder handles, at about 1.4 s per search.

Ten rerank candidates matched twenty (isis-live 0.925 vs 0.924, SciFact 0.715 both, LongMemEval 0.931 vs 0.922, Atlas
0.880 vs 0.887) at about 40% less latency (p50 330 to 430 ms instead of 520 to 670 ms on CPU), so 10 is the default.

### Round 6: fusion, model profiles, and what adding queries costs

Round 6 finished tuning the generic settings and measured two larger ideas. The history command's view of the
default configuration (all-minilm, Hybrid, no reranker) is flat to slightly up against round 5, which is expected: the
round's main shipped change for these numbers is the fusion constant.

| Hybrid nDCG@10, mean over four datasets | RRF 20 | RRF 60 |
|---|---|---|
| all-minilm, text weight 0.3 | 0.818 | |
| all-minilm, text weight 0.5 | **0.827** | 0.826 |
| all-minilm, text weight 0.7 | | 0.807 |
| nomic-embed-text, text weight 0.5 | 0.815 | 0.808 |

Both models did best at a text weight of 0.5 with an RRF constant of 20, so 20 became the generic default and no
model needed its own fusion weights. nomic-embed-text then got its own chunk size from a sweep:

| nomic-embed-text chunk cap | isis-live | Atlas | SciFact | LongMemEval | Mean |
|---|---|---|---|---|---|
| **128** | **0.881** | 0.803 | **0.704** | **0.917** | **0.826** |
| 256 (the generic cap) | 0.868 | **0.813** | 0.683 | 0.866 | 0.808 |
| 384 | 0.858 | 0.810 | 0.671 | 0.886 | 0.806 |
| 512 | 0.864 | 0.788 | 0.659 | 0.879 | 0.797 |

At 128 tokens nomic ties all-minilm on average (0.826 vs 0.827) at twice the vector size, so all-minilm stays the
default and nomic's profile carries the 128-token cap.

**gpt-oss-20b as a prompted reranker** is the best ranking measured (table above), and its score is the first signal
that separates answerable from unanswerable questions well: AUROC 0.991 on isis-live and 0.960 on Atlas, against 0.57
to 0.79 for every earlier score. It costs 6.6 to 10.2 s per search (p50), so it suits an opt-in high-precision mode or
a check before answering, not the default.

**Query decomposition** (gemma3:4b splitting a question into up to three parts, each searched and fused at equal
weight with the original) lowered every dataset and chat accuracy:

| | Without | With decomposition |
|---|---|---|
| isis-live Hybrid nDCG@10 | 0.879 | 0.844 (multi 0.834 → 0.744) |
| Atlas | 0.831 | 0.764 |
| SciFact | 0.682 | 0.666 |
| LongMemEval | 0.912 | 0.901 |
| Chat answer accuracy (isis-live) | 0.944 | 0.900 |
| Chat latency p50 | 2.2 s | 3.0 s |

The parts displaced memories the original question already ranked well. Chat decomposition is off by default; the
search options (`additionalQueries`, `decompose`) remain for callers that supply good sub-queries, and any later
multi-query work will weight the original query above the rest.

Chat on isis-live with round 6's defaults answered 94.4% of answerable questions correctly, declined all 20
unanswerable ones, and had the evidence in the prompt for 99.1%, the same as round 5 within the judge's noise.

### Round 7: follow-up questions in chat

Chat used to see only the latest message, so a follow-up such as "can I use it today?" was searched as written. Round
7 lets the caller send the earlier messages; the chat model rewrites the follow-up into a standalone query, retrieval
searches both forms, and the answer prompt shows the conversation. The new `isis-live-followups` dataset asks 32
follow-ups over the isis-live memories, each after one earlier exchange: 26 that refer back ("does it compute the
embeddings itself?") and 6 that shift topic ("what about the dashboard?"). Both arms send the history to the answer
model; only the rewrite differs. gemma3:4b, one question at a time.

| isis-live-followups | Rewrite off, 8 memories | Rewrite on, 8 memories | Rewrite off, 3 memories | Rewrite on, 3 memories |
|---|---|---|---|---|
| Evidence reached the prompt | 0.969 | **1.000** | 0.953 | **1.000** |
| Answer accuracy | 0.938 | 0.938 | 0.969 | **1.000** |
| Citation recall | 0.891 | **0.953** | 0.953 | **0.984** |
| Citation precision | 0.745 | **0.794** | 0.665 | **0.712** |
| Latency p50 | 1.6 s | 2.3 s | 1.9 s | 2.4 s |

Every retrieval miss without the rewrite was a follow-up that referred back; topic shifts named their new subject and
were found either way. With only 24 memories and 8 retrieved, a third of the corpus reaches the prompt regardless, so
the 3-memory runs are the more telling ones; a larger corpus would show a larger gap. The rewrite costs about 0.7 s
(one short model call) and runs only for questions sent with history. gemma3:4b often rewrote into keyword lists
rather than questions ("which four?" became "four provider-neutral DAL drivers"), which still searches well because
the original question is searched too.

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
| Reranker score (round 3, ms-marco-MiniLM-L-6-v2) | 0.63 | 0.66 | 0.73 |

Neither is reliable enough to act as an abstention gate. The fused score is a rank-fusion score, so every query's
best hit lands near 1.0 whether or not it is relevant. The raw vector similarity, which Isis now returns on every
hit, is the better signal, and it is usable as a soft filter on LongMemEval.

Round 3 added a reranker with 0..1 scores, and it did not change this picture. The small MS MARCO cross-encoder ranks
well but scores many conversational, correctly answerable questions near zero. On isis-live, a cutoff that empties the
results for 30% of unanswerable questions also empties them for 16% of answerable ones; on Atlas, every cutoff
removes answerable and unanswerable questions at about the same rate. The cutoff (`minRerankScore`) is implemented
and tested, but it has no default. A larger or better-calibrated reranker is what this would need.

### Latency

Hybrid search p50 in round 2 was 42 ms on isis-live and Atlas, 38 ms on LongMemEval, and 95 ms on SciFact. SciFact is
slower because any-term text matching over 11,000 chunk documents ranks thousands of matches per query; the full-text
index is used, and the cost is the ranking itself. Round 2 did not change the search path's cost.

In round 3 without a reranker, Hybrid p50 was 33 ms on isis-live, 37 ms on Atlas, 52 ms on LongMemEval, and 89 ms on
SciFact. The supersession lookup adds one indexed query per search, and the lookup cache removes several. The
reranker is the expensive part: on TEI's CPU image, scoring 20 candidates put p50 at 610 to 870 ms. A GPU-backed
reranker, fewer candidates (`rerankCandidates`), or shorter passages bring that down.

## Chat with memory

The isis-live questions, asked through the Isis chat route and graded by an independent model call:

| | Round 0 | Round 1 | Round 2 | Round 3 | Round 3 + rerank |
|---|---|---|---|---|---|
| Answer accuracy (90 answerable questions) | 0.678 | 0.956 | **0.989** | 0.977 | 0.955 |
| Evidence reached the prompt | 0.918 | 0.918 | 0.974 | **0.991** | 0.994 |
| Accuracy when the evidence was in the prompt | 0.706 | 0.976 | 0.989 | 0.977 | 0.955 |
| Correctly declined (20 unanswerable questions) | 0.95 | 0.90 | 0.85 | **1.00** | **1.00** |
| Citation precision / recall | 0.52 / 0.72 | 0.65 / 0.85 | 0.66 / 0.87 | 0.64 / **0.92** | 0.63 / 0.92 |
| Latency p50 | 8.2 s | 14.9 s | 15.4 s | 6.6 s (remote GPU) | 4.4 s (remote GPU) |

Round 1's jump came from one change: chat used to ground the model on a 240-character preview of each memory, so any
answer past a memory's first sentence was invisible. Round 2's gain came from retrieving 8 memories instead of 5,
which put the evidence in the prompt for 97% of questions instead of 92%.

Declining unanswerable questions got slightly worse in each of the first three rounds, 19, then 18, then 17 of 20,
even though round 2's prompt tells the model to say when the answer is not in memory. A larger, more complete context
gives a small model more near-relevant material to talk itself into an answer with. The latency increase is the
larger prompt on a laptop GPU.

Round 3 declined every unanswerable question that was graded. The cause is not isolated: two round-3 changes alter
the prompt (replaced memories are labelled as outdated, and chat follows up to two links from the retrieved memories,
which put the evidence in the prompt for 99% of answerable questions), and the model was served differently. Round 3's chat ran against the same model, `gemma3:4b`, served from a remote
GPU (an NVIDIA GB10) instead of the laptop, one question at a time; the laptop's GPU was shared with other workloads
during this round. Its latency is therefore not comparable with earlier rounds. The remote endpoint rejected a few
requests at capacity, so 1 of 110 questions errored and 3 answers went ungraded; the rates above exclude them. The
answer-accuracy dip from 0.989 to 0.977 is two questions, within the judge's noise.

Rounds 4 and 5 (same questions, gemma3:4b on the GPU host, one question at a time):

| | Round 4 | Round 4 + rerank | Round 5 | Round 5 + gemma rerank |
|---|---|---|---|---|
| Answer accuracy | **1.000** | 0.922 | 0.944 | 0.900 |
| Correctly declined | 0.90 | 0.85 | 0.95 | **1.00** |
| Evidence reached the prompt | **1.000** | 0.994 | 0.991 | 0.941 |
| Citation precision / recall | 0.55 / **1.00** | 0.62 / 0.89 | 0.65 / 0.87 | 0.60 / 0.83 |

Round 5 had 0 errors and 0 ungraded answers. Chat grounding is saturated: the evidence reaches the prompt for 99 to
100% of answerable questions, so answer accuracy moves within the 4B judge's noise (0.94 to 1.00 across rounds 2 to
5). Reranking has not improved chat in any round; the round-5 gemma reranker dropped evidence from 6% of prompts.

The reranked scope did not improve chat. With link expansion the evidence already reaches the prompt for 99% of
answerable questions, so better ordering has little left to add, and 0.955 against 0.977 is two questions apart,
again within the judge's noise. Its lower latency is the shorter reranked context (the reranker keeps the best 8 of
20 candidates, trimmed to the caller's budget).

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

## Environment notes for rounds 4 to 6

Rounds 4 and 5 met two environment problems. Other workloads shared the laptop's Ollama for most of round 4 and
early round 5, slowing ingest by up to 30 times, and the machine ran short of ephemeral ports once (1 of 2,891
LongMemEval sessions). Round 5 then moved every model call to a GPU host. Its shared model router refused requests
at capacity (429) well below the host's real throughput, so round 5 finally ran against the host's Ollama directly,
bounded at 16 embedding requests in flight (the highest level measured clean). The harness now retries upserts that
Isis answers with 503, and the benchmark runs from a renamed copy of the harness so that other sessions stopping
their own benchmark processes by name do not stop it. Round 6 ran the same way, with reranking, chat, and
decomposition calls one at a time.

## What's next

[RETRIEVAL_IMPROVEMENTS.md](../RETRIEVAL_IMPROVEMENTS.md) lists every fix considered, scored for value and simplicity,
with what landed in each round and the current ranked list. After round 6 the results point at:

- **An opt-in high-precision mode** built on a larger chat model as the reranker, with a relevance cutoff chosen from
  its score distributions; it is the strongest ranking and "nothing relevant" signal measured.
- **Query rewrite that keeps the original query dominant**: a low-weight hypothetical answer and keyword expansion
  for scopes without a reranker (rewriting chat follow-ups landed in round 7).
- **RecallDB single-call hybrid search**, planned in the RecallDB repository.
- **Wider evaluation**: more BEIR datasets with published baselines (NFCorpus, FiQA, ArguAna, SciDocs), the full
  LongMemEval_S, and a larger multi-turn set (Atlas-sized) to measure follow-up rewriting where retrieval is harder.
