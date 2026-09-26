# Retrieval improvements

The first benchmark baseline (`benchmarks/RESULTS.md`) showed five weak areas in Isis retrieval:

- Superseded facts: the old version of a fact often outranks its replacement.
- Heavy paraphrase: the right memory is ranked first only a third of the time on Atlas.
- Questions that need 2–3 memories: all of them rarely reach the top 10 together.
- No usable "nothing relevant" signal: unanswerable questions score close to real hits.
- Keyword latency on large corpora: an any-term text query ranks every match.

The table lists every fix considered, each scored 1–10 for value (expected effect on the benchmark and on real
agents) and 1–10 for simplicity (effort and risk to build and ship). The score is value plus simplicity, and ties
are broken by value. Everything with a simplicity of 8 or more is scheduled for the current round; the rest are
candidates for later rounds. The Status column is updated as work lands, and measured results go in the table at
the end.

| Rank | Fix | Weak area | Value | Simplicity | Score | Status |
|---|---|---|---|---|---|---|
| 1 | Mark a memory as replacing another (a `supersedes` field on upsert); search demotes or hides the replaced memory | Superseded facts | 9 | 6 | 15 | Done (round 3) |
| 2 | Use a stronger default embedding model (choose by benchmark); needs a re-embed | Paraphrase | 8 | 7 | 15 | Later |
| 3 | Recency boost in ranking: break close scores in favor of the newer memory | Superseded facts | 7 | 8 | 15 | Done (round 2) |
| 4 | Guide agents to update, not duplicate: default instructions tell agents to re-upsert the existing slug when a fact changes | Superseded facts | 6 | 9 | 15 | Done (round 2) |
| 5 | Raise chat's default retrieval depth (topK 5 → 8) | Multi-memory | 5 | 10 | 15 | Done (round 2) |
| 6 | Follow memory links: pull in memories the top hits link to with `[[slug]]` | Multi-memory | 7 | 7 | 14 | Done (round 3); off by default in search, 2 links in chat |
| 7 | Embed a header on each chunk (title and summary, not just body text) | Paraphrase | 6 | 8 | 14 | Done (round 2) |
| 8 | RecallDB: skip or cap the count query on ranked text and hybrid searches | Keyword latency | 6 | 8 | 14 | Already addressed upstream (see note) |
| 9 | Stricter chat prompt: answer only what memory states, cite every claim, otherwise say it isn't in memory | No "nothing relevant" signal | 5 | 9 | 14 | Done (round 2) |
| 10 | Return the fused score and each leg's raw score, and add a `minScore` filter | No "nothing relevant" signal | 5 | 8 | 13 | Done (round 2) |
| 11 | Rerank the top results with a cross-encoder (a new endpoint type) | Paraphrase, multi-memory, no "nothing relevant" signal | 8 | 4 | 12 | Done (round 3) |
| 12 | Check for duplicates at write time: search for similar memories on upsert and flag or auto-link likely replacements | Superseded facts | 8 | 4 | 12 | Done (round 3); flags only, no auto-link |
| 13 | "Nothing relevant" cutoff on the reranker's score (needs #11) | No "nothing relevant" signal | 7 | 5 | 12 | Done (round 3); off by default (see results) |
| 14 | Diversify results (MMR) so near-duplicate chunks don't crowd out other memories | Multi-memory | 5 | 7 | 12 | Done (round 3); off by default |
| 15 | Use RecallDB's single-call hybrid search instead of Isis's own two-call fusion (needs an SDK release that exposes the hybrid options) | Keyword latency | 6 | 5 | 11 | Later |
| 16 | Split multi-part questions into sub-queries with the chat model | Multi-memory | 6 | 4 | 10 | Later |
| 17 | Query expansion (search with a model-drafted hypothetical answer) | Paraphrase | 5 | 5 | 10 | Later |
| 18 | Cache auth, scope and endpoint lookups (saves ~11 ms per search, but delays credential revocation) | Keyword latency | 4 | 6 | 10 | Done (round 3); 10 s time to live |

**Note on #8.** The table was scored against RecallDB's pre-patch search code, which ran a separate `COUNT(*)` over
the whole match set on every text query. The patched RecallDB (`d8ce32c`) computes the total with
`COUNT(*) OVER ()` in the same statement and only issues a separate count when a page comes back empty. The
remaining cost is inherent: an any-term query must rank every match to return the best ones first. Further gains
there would come from #15 or a cheaper pre-ranking pass, not from the count.

## Implementation notes

**#3 Recency boost.** Hybrid fusion moved into a pure, unit-tested `HybridFusion` class
(`src/Isis.Core/Stores/RecallDb/HybridFusion.cs`). Candidates from both legs are ranked by their memory's write time,
newest first. That ranking is fused as a third reciprocal-rank signal weighted by `MemorySearchQuery.RecencyWeight`
(default 0.1, 0 disables, also accepted by REST and MCP `memory_search`). Isis re-creates a memory's store documents
on every upsert, so write time is the time of the last update, which is what "the newer fact" means. At 0.1 the signal
is small next to the two relevance legs (0.5 each by default). It settles near-ties and cannot lift a clearly weaker
match over a strong one, which the unit tests pin down.

The default was chosen by sweeping the weight over 0, 0.03, 0.05, 0.1, and 0.2 on all four datasets. The table in
[Measured results](#measured-results) has the numbers. Recency helps where write order means something (Atlas
superseded facts improve steadily with the weight) and costs a little where it does not (SciFact is a bulk import in
arbitrary order, so recency there is noise). 0.1 takes most of the superseded-fact gain for about a point of
nDCG on the bulk import. Scopes filled by a bulk import should pass `recencyWeight: 0`.

**#4 Update, don't duplicate.** The seeded "Storing memories" and "Recall" instructions now tell agents to search for
the existing memory when a fact changes and re-upsert it under the same slug and category, rather than adding a
second memory that keeps surfacing next to the new one. Seeded instructions apply to new tenants. Existing tenants
keep their current instructions until an admin edits them.

**#5 Chat retrieval depth.** `ChatRequest.TopK` now defaults to 0, meaning "use the server default". The default is
`MemoryChatService.DefaultTopK`, which is 8 (validated 1..100). An explicit `topK` still wins.

**#7 Chunk headers.** Each chunk is embedded as `title: summary` followed by the chunk text
(`MemoryChunk.EmbeddingText`). The stored chunk text is unchanged, so snippets, chat grounding, and full-text search
see exactly what they did before. The chunker reserves the header's tokens from the per-chunk budget, and it
truncates a header longer than `MemoryChunker.HeaderBudgetFraction` of the budget (default 0.25) at a word boundary.
Existing memories pick the header up the next time they are written. Re-embedding a scope requires re-upserting its
memories.

**#8** needed no change; see the note under the table.

**#9 Stricter chat prompt.** The system prompt now restricts answers to facts stated in the provided memories, forbids
general-knowledge fill-in and guessing, requires a slug citation for every claim, and asks for a plain "not in memory"
(plus any related information) when the memories do not answer the question.

**#10 Scores and threshold.** Hybrid hits now carry the fused score normalized to 0..1 (1.0 means ranked first by
every signal), instead of whichever raw leg score happened to be seen first. They also carry the evidence behind it:
`vectorScore`, `textScore`, `vectorRank`, `textRank`. Semantic and keyword hits carry their raw leg score in `score`
and in the matching evidence field. `MemorySearchQuery.MinScore` (REST `minScore`, MCP `minScore`) drops hits below a
threshold in every mode.

**Robustness fixes found while measuring.** The re-run surfaced four ingest problems. All four are fixed here and
covered by tests or by the benchmark itself:

- **Concurrent scope provisioning.** RecallDB derives a collection's index names from the time component of its id,
  so two collections created in the same instant collide and the second loses its indexes. Isis also never
  recovered from that failed create: the scope kept retrying and failing on the duplicate name. It cost 183 of 2,891
  LongMemEval ingests once the harness provisioned 8 scopes in parallel. Isis now serializes collection creation
  per tenant, and if a create fails it adopts an existing collection named for the scope. Afterwards: 0 provisioning
  failures. The index-name collision itself is a RecallDB bug (`GetIndexIdentifier` in `DynamicTableQueries.cs`)
  and should be fixed there.
- **Invalid Unicode.** Unpaired surrogates in a memory's title, summary, or body are replaced with U+FFFD on upsert.
  Previously one stray code unit made the whole memory unstorable.
- **Emoji split by the chunker.** TextChunker can split text at a token boundary inside a surrogate pair (an emoji),
  and the half-pair then fails normalization in the tokenizer. Isis now chunks a same-length stand-in copy of the
  text and slices the real chunks from the original, keeping every pair whole. This is also a TextChunker bug worth
  fixing upstream.
- **Redundant tail chunks.** Fixed-token chunking with overlap emitted runs of tiny trailing chunks that repeat the
  end of the previous chunk: 2.7% of all chunks on LongMemEval sessions, some with unresolvable offsets. Isis drops
  any chunk wholly contained in the one before it, which cuts LongMemEval chunk count by 9% with no information
  lost. Again, the root cause is in TextChunker.

**Tests.** A new `RetrievalSuite` (23 cases) covers:

- fusion: normalization, union behavior, weight extremes, recency tie-breaking, recency not overriding relevance,
  recency disabled at 0, per-memory recency, and argument validation
- chunk headers: prefixing, staying inside the model budget, truncation, and the no-header path
- defaults and clamping for `RecencyWeight` and `MinScore`
- `MinScore` filtering through `MemoryService`
- the chat retrieval default and its validation
- the chat prompt text actually sent to the model (captured by the stub HTTP handler)
- the update-not-duplicate instruction text
- the Unicode and chunking fixes: surrogate sanitizing, an emoji-heavy body chunked with every pair kept whole, and
  no chunk wholly contained in its predecessor

The MCP suite gained `memory-search-min-score`, which calls the `memory_search` tool over the wire and checks that
`minScore` reaches the search.

## Round 3 implementation notes

Round 3 took #1, #6, #11, #12, #13, #14, and #18.

**#1 Supersession.** A memory's `supersedes` lists the slugs (or ids) of memories it replaces. On upsert the server
marks each named memory with `supersededBy`, releases memories dropped from the list, and, when a memory is written
after the memory that replaces it (a re-import in any order), marks it on create. Deleting the replacement makes the
replaced memory current again. The columns come from migration `2026-09-24-memory-supersession` (an `ALTER TABLE`, so
existing rows simply read as current). After retrieval, `SearchRefiner` (`src/Isis.Server/Services/SearchRefiner.cs`)
resolves each hit to its memory row with one indexed query, follows replacement chains to the current memory, and
applies `superseded`: `Demote` (default) puts the replacement where the stale memory ranked and the stale memory
right after it, adding the replacement when the search missed it; `Hide` drops the stale memory; `Include` only marks
it. Chat labels a replaced memory as outdated in the prompt.

**#6 Link expansion.** `linkExpansion` adds up to N memories linked from the results, through the `links` list or
`[[slug]]` references in the body, placed after the result that links to them and marked `linkedFrom`. It is extra to
`topK`. It is off for search, because in a ranked list the linked memories push lower-ranked relevant hits down
(isis-live Hybrid nDCG 0.877 to 0.852 with 2 links), and on in chat (`retrieval.chatLinkExpansion`, default 2), where
extra context is what it is for.

**#11 Reranking.** A new endpoint kind, `Rerank` (`rep_` ids), with the `Tei` and `Cohere` API formats
(`RerankService`). A scope names a `rerankEndpointId` and `rerankCandidates` (default 20). A reranked search retrieves
that many candidates with a 1,200-character passage each, scores title plus passage with the cross-encoder, keeps the
best `topK`, and trims snippets back to the caller's budget. `rerank: false` opts out per query. If the reranker fails,
the search falls back to retrieval order with a notice rather than failing. The benchmark used
`cross-encoder/ms-marco-MiniLM-L-6-v2` on TEI's CPU image, which the bench compose file now starts under the `rerank`
profile.

**#12 Similar memories on upsert.** After a write to a scope with semantic search, the server runs a vector search with
the new memory's first chunk embedding and returns up to 3 memories at or above `retrieval.duplicateSimilarityThreshold`
in `similarMemories`. It flags; it does not link or merge. The threshold was set from the Atlas data: a memory and its
replacement score 0.55 to 0.88 with all-minilm, and distinct but related memories reach 0.89, so no threshold
separates them cleanly. At 0.9 nothing was flagged; the default is 0.85, where 3 of the 9 known pairs are flagged
against 2 other pairs across 170 memories. Treat the flags as prompts for the writer to check.

**#13 Cutoff.** `minRerankScore` (query) and `rerankMinScore` (scope) drop reranked hits below a score. When the cutoff
removes every candidate, chat grounds on no memories so the model says the answer is not in memory. The mechanism
works, but the measured rerank score does not separate answerable from unanswerable questions well enough to set a
default (see results), so it is off unless configured.

**#14 Diversity.** `diversity` reorders results by maximal marginal relevance. Similarity between results is word
overlap rather than embedding similarity, because RecallDb.Sdk 0.2.1 (the only published version) cannot request
stored vectors. It works on every store type. It is off by default: it helped LongMemEval and cost a point elsewhere.

**#18 Lookup cache.** `LookupCache` holds credentials, users, scopes, and endpoints for `cache.ttlSeconds` (default 10).
Every successful write through the node invalidates the affected record type centrally (so MCP-proxied writes are
covered), and a load that raced an invalidation is never cached. The time to live bounds how long a credential revoked
on another node keeps working here.

**Also fixed.** MCP `scope_update` sent only the name and description to a full-replace `PUT`, which cleared the
scope's embedding endpoint and chunking settings. It now reads, merges, and writes. `scope_create` over MCP now
forwards the chunking and rerank settings its documentation already listed.

**Tests.** A new `RefinementSuite` (35 cases) covers query and scope defaults, both migrations applied to an older
schema, every supersession write path, Demote/Hide/Include and chain following through search, link expansion from
both `links` and `[[slug]]`, MMR ordering and validation, the TEI and Cohere request and response shapes, rerank
ordering, both cutoffs, opt-out, and outage fallback, the upsert similarity report, and the cache (time to live,
invalidation racing a load, write-path invalidation, disabled mode). The MCP suite gained `memory-upsert-supersedes`
and `scope-update-keeps-settings`. All 423 cases pass.

## Measured results

### Round 3

Hybrid nDCG@10, same datasets and machine as round 2; Atlas was re-ingested with its replacement facts declared.
Full tables are in `benchmarks/RESULTS.md`.

| Dataset | Round 2 | Round 3 default | Round 3 + rerank | Notes |
|---|---|---|---|---|
| isis-live | 0.877 | 0.877 | **0.925** | |
| Atlas | 0.804 | 0.809 | **0.894** | Superseded 0.777 → 0.802 (supersession) → **0.875** (rerank); lexical 0.740 → **0.935**; paraphrase 0.707 → **0.833** |
| SciFact | 0.678 | 0.678 | **0.700** | |
| LongMemEval-S | 0.907 | 0.907 | **0.912** | 0.918 with `diversity: 0.3` |

Chat (isis-live, gemma3:4b served remotely): correct declines on unanswerable questions 0.85 → **1.00**, evidence in
the prompt 0.974 → **0.991**, answer accuracy 0.989 → 0.977 (two questions, within judge noise). The reranked scope
gave 0.955 accuracy and 1.00 declines.

What round 3 showed:

- **The reranker is the biggest lever measured so far**, including on the lexical regression from #7, but costs
  0.6 to 0.9 s per search on a CPU cross-encoder.
- **The small reranker cannot say "nothing relevant".** Its score separates answerable from unanswerable questions at
  AUROC 0.63 to 0.73, no better than the fused score, so #13's cutoff ships without a default.
- **Similarity alone does not detect replacements** with all-minilm (replacement pairs 0.55 to 0.88, unrelated
  neighbors up to 0.89), so #12 flags candidates rather than linking them.
- **Diversity and link expansion are situational**: diversity helps only overlapping chat-session corpora, and link
  expansion is for chat context, not ranking.

### Round 2

Hybrid nDCG@10 before and after this round, from `benchmarks/RESULTS.md` (same machine, same datasets, every scope
re-ingested):

| Dataset | Before | After | Notes |
|---|---|---|---|
| isis-live | 0.859 | **0.877** | Chunk headers; recency is neutral on this undated corpus |
| Atlas | 0.803 | 0.804 | Superseded facts 0.718 → **0.777** (hit@1 0.40 → 0.55); paraphrase 0.673 → 0.707; **lexical 0.833 → 0.740** |
| SciFact | 0.688 | 0.678 | A bulk import, so recency is noise here (0.693 with recency off) |
| LongMemEval-S | 0.906 | 0.907 | Semantic 0.853 → 0.887; ingest 19% faster with redundant chunks dropped |

Chat (isis-live): answer accuracy 0.956 → **0.989**, and evidence reached the prompt for 0.918 → 0.974 of questions,
from #5. Correct declines on unanswerable questions went 18 → 17 of 20 despite #9's stricter prompt. Agent benchmark:
96% with Isis vs 17% without.

The "nothing relevant" signal (#10) is weaker than hoped. As an answerable-vs-unanswerable classifier (AUROC, 0.5 =
chance), the fused score reaches 0.57 to 0.67 and the raw `vectorScore` 0.64 to 0.78. That is useful as a soft filter,
not as an abstention gate; #11 and #13 are the real fix.

What this changed for round 3:

- **#1 (explicit supersession) moves up.** Recency got superseded facts from 40% to 55% ranked first, and the sweep
  shows no weight gets much further without hurting undated corpora.
- **The lexical regression from #7 needs attention.** For example, embed the header only on chunks after the first,
  or weight the text leg up for queries that look like identifiers.
- **#2 and #11 remain the biggest levers** for paraphrase (still 45% ranked first on Atlas) and for abstention.

## Remaining work

Not yet done from the table, in score order:

| Rank | Fix | Why it still matters after round 3 |
|---|---|---|
| 2 | Stronger default embedding model (needs a re-embed) | Without a reranker, paraphrase is still 45% ranked first on Atlas; a better model helps every search without the reranker's latency |
| 15 | RecallDB single-call hybrid search | Would drop Isis's two-call fusion; blocked on an SDK release that exposes the hybrid options |
| 16 | Split multi-part questions into sub-queries | Multi-memory questions are the weakest type even with the reranker (Atlas 0.845) |
| 17 | Query expansion (hypothetical-answer search) | Paraphrase, for scopes without a reranker |

New items found in round 3:

| Fix | Why |
|---|---|
| A larger or better-calibrated reranker, benchmarked for abstention | The small MS MARCO cross-encoder ranks well but its score cannot gate "nothing relevant" (AUROC 0.63 to 0.73) |
| GPU-served reranking, or fewer and shorter candidates by default | CPU reranking costs 0.6 to 0.9 s per search |
| Stored vectors in RecallDB search results through the SDK | Lets diversity (#14) and the similarity check compare embeddings of whole memories instead of words and first chunks |
| Replacement detection that goes beyond similarity (for example, ask the chat model whether a flagged memory is superseded) | Similarity alone cannot separate replacements from related memories with all-minilm |
| Retry on 429 from inference and judge endpoints | Shared remote endpoints reject requests at capacity; chat currently returns 502 |

## Round 4 findings and the updated table

Round 4 moved to TextChunker 0.3.1 (span-based chunking and WordPiece counts that match the embedding runtime) and
removed the Isis workarounds it made unnecessary. Atlas Hybrid nDCG@10 fell from 0.809 to 0.802, with detail
(0.933 to 0.880), category (0.982 to 0.945), and multi (0.810 to 0.783) questions dropping. The cause is not a
chunking defect. TextChunker 0.2.2 undercounted tokens, so 53 of Atlas's 80 multi-chunk memories had a chunk
rejected by Ollama in round 3 and were re-chunked by Isis's fallback at 75% of the budget. Round 3 therefore stored
smaller chunks by accident, and 0.3.1, whose chunks all fit the first time (0 of 81 rejected), stores chunks of about
240 tokens instead of about 190. Setting the chunk budget to 190 tokens on purpose restores and exceeds round 3:

| Atlas Hybrid nDCG@10 | Round 3 | Round 4 | Round 4, 190-token chunks | Round 4, 128-token chunks |
|---|---|---|---|---|
| category | 0.982 | 0.945 | **0.982** | 0.945 |
| confusable | 0.863 | 0.868 | 0.879 | 0.888 |
| detail | 0.933 | 0.880 | **0.953** | 0.898 |
| lexical | 0.746 | 0.754 | **0.776** | 0.732 |
| multi | 0.810 | 0.783 | **0.822** | 0.796 |
| paraphrase | 0.707 | 0.726 | 0.717 | 0.684 |
| superseded | 0.802 | 0.800 | **0.841** | 0.856 |
| **Overall** | 0.809 | 0.802 | **0.827** | 0.799 |

The updated table below keeps every open item from the original table and adds what rounds 3 and 4 found, scored the
same way (value and simplicity 1 to 10, score is their sum, ties broken by value).

| Rank | Fix | Weak area | Value | Simplicity | Score | Source |
|---|---|---|---|---|---|---|
| 1 | Default chunk budget below the model limit (about 75% for all-minilm), chosen by a sweep on all datasets; `chunkMaxTokens` still overrides | Detail, multi, category (the round-4 regression) | 7 | 9 | 16 | Round 4 regression |
| 2 | Stronger default embedding model (choose by benchmark; needs a re-embed) | Paraphrase | 8 | 7 | 15 | Original #2 |
| 3 | Retry with backoff on 429 and 503 from inference and judge endpoints | Reliability on shared endpoints | 4 | 8 | 12 | Round 3 |
| 4 | Benchmark larger or better-calibrated rerankers for abstention (for example bge-reranker-base or -large) | No "nothing relevant" signal | 7 | 5 | 12 | Round 3 |
| 5 | Cheaper reranking by default: fewer candidates (10), shorter passages, GPU-served reranker in the reference stack | Rerank latency (0.6 to 1.1 s on CPU) | 6 | 6 | 12 | Round 3 |
| 6 | RecallDB single-call hybrid search (needs an SDK release that exposes the hybrid options) | Keyword latency | 6 | 5 | 11 | Original #15 |
| 7 | Split multi-part questions into sub-queries with the chat model | Multi-memory | 6 | 4 | 10 | Original #16 |
| 8 | Query expansion (search with a model-drafted hypothetical answer) | Paraphrase | 5 | 5 | 10 | Original #17 |
| 9 | Replacement detection beyond similarity: ask the chat model whether a flagged similar memory is superseded, and offer the `supersedes` link | Superseded facts | 6 | 4 | 10 | Round 3 |
| 10 | Stored vectors in RecallDB search results through the SDK, so diversity and the similarity check compare embeddings (whole memory, not first chunk) | Multi-memory, superseded | 4 | 4 | 8 | Round 3 |

Item 1 should be measured on isis-live, SciFact, and LongMemEval before it becomes the default. LongMemEval's single
preference-question drop in round 4 (8 questions) is covered by the same sweep.

## Round 5 findings and the current table

Round 5 took items 1 to 5 of the round-4 table. Every model call ran on one GPU host (all-minilm, nomic-embed-text,
and gemma3:4b served by Ollama), so the numbers below are not affected by the laptop GPU contention of earlier rounds.

| Round-4 item | Result |
|---|---|
| 1. Default chunk budget below the model limit | **Done.** Chunks default to 75% of the model budget, capped at 256 tokens. Atlas 0.802 → 0.831 and LongMemEval 0.895 → 0.912; superseded facts 0.800 → 0.876 without a reranker |
| 2. Stronger embedding model | **Measured, not adopted.** nomic-embed-text wins Semantic search (SciFact 0.658 → 0.693) but ties or loses Hybrid (isis-live 0.879 → 0.868, Atlas 0.831 → 0.813, LongMemEval 0.912 → 0.866) at twice the vector size. all-minilm stays the default; task prefixes are now applied for models that need them |
| 3. Retry on 429/502/503 | **Done.** Model calls retry with backoff; an endpoint still at capacity is reported as 503, not 400 |
| 4. Larger or calibrated reranker | **Measured, not adopted.** bge-reranker-base ranks worse than ms-marco-MiniLM on every dataset at 4 to 6 times the latency, with no better abstention AUROC. gemma3:4b prompted as a reranker lowers every dataset (isis-live 0.879 → 0.760, Atlas 0.831 → 0.631) |
| 5. Cheaper reranking | **Done.** Default candidates 20 → 10: equal quality on average (LongMemEval 0.922 → 0.931, Atlas 0.887 → 0.880) at about 40% less latency. GPU and CPU reranker profiles added to both compose files |

What five rounds taught:

- **A cross-encoder reranker is the largest lever measured** (+0.02 to +0.08 nDCG), and small chat models are not a
  substitute for one.
- **Hybrid fusion caps what a better embedding model can add.** The keyword leg already recovers most of what
  nomic gains on its own, so embedding upgrades need the fusion weights re-tuned with them.
- **Chunk size matters more than expected**, and round 3's apparent gains partly came from an accidental re-chunk.
  Retrieval-sized chunks (about 190 tokens here) beat chunks that fill the model window.
- **No score separates answerable from unanswerable questions well** (AUROC 0.57 to 0.79 across fused, vector, and
  rerank scores). Chat abstention now comes mostly from the prompt and grounding, where it reached 0.90 to 1.00.
- **Chat grounding is saturated**: evidence reaches the prompt for 99 to 100% of answerable questions, and reranking
  did not improve chat.

The current table replaces the round-4 table. Scores are value plus simplicity, each 1 to 10.

| Rank | Fix | Weak area | Value | Simplicity | Score |
|---|---|---|---|---|---|
| 1 | Ship the cross-encoder by default: seed a Rerank endpoint when the reference stack's reranker is running, and attach it to new RecallDB scopes | Paraphrase, lexical, superseded (all improve 0.05 to 0.2 with it) | 8 | 6 | 14 |
| 2 | Re-tune hybrid fusion (text weight, RRF constant) per embedding model, and re-test nomic with tuned weights | Embedding upgrades that do not carry into Hybrid | 6 | 8 | 14 |
| 3 | Choose the chunk fraction and cap per embedding model by sweep (all-minilm is tuned; nomic used the same 256-token cap) | Detail, multi | 5 | 8 | 13 |
| 4 | Test a larger chat model as a prompted reranker (gpt-oss-20b), now that prompted reranking exists | Rerank quality without a cross-encoder | 4 | 9 | 13 |
| 5 | GPU-served reranking in production deployments (the CPU cross-encoder adds 0.3 to 0.5 s per search at 10 candidates) | Rerank latency | 5 | 6 | 11 |
| 6 | RecallDB single-call hybrid search (needs an SDK release exposing the hybrid options) | Keyword latency | 6 | 5 | 11 |
| 7 | Split multi-part questions into sub-queries | Multi-memory (the weakest type: Atlas 0.81, LongMemEval preference questions 0.55 to 0.74) | 6 | 4 | 10 |
| 8 | Abstention from a combined, calibrated signal (vector score, rerank score, and score gap) instead of any one score | "Nothing relevant" | 6 | 4 | 10 |
| 9 | Query expansion with a drafted hypothetical answer | Paraphrase without a reranker | 4 | 5 | 9 |
| 10 | Replacement detection that goes beyond similarity (a larger model judging flagged pairs; the 4B model's relevance judgments were unreliable) | Superseded facts | 5 | 4 | 9 |
| 11 | Stored vectors in RecallDB search results through the SDK | Diversity and similarity on whole memories | 4 | 4 | 8 |

## Round 6 findings and the current table

Round 6 took items 1 to 7 of the round-5 table, except item 6 (single-call hybrid search), which needs a RecallDB
release and now has its own plan (`HYBRID_SEARCH_IMPROVEMENTS.md` in the RecallDB repository). Item 10 was dropped: a
deployment that chooses a small model accepts that model's judgment quality, and Isis should not add machinery to work
around it. Model-specific settings stay generic: `EmbeddingModelProfiles` holds sensible defaults for every model plus
overrides only where a benchmark showed a known model needs one. Every model call ran on the GPU host's Ollama.

| Round-5 item | Result |
|---|---|
| 1. Ship the cross-encoder by default | **Done.** The reference stack's `rerank` (CPU) or `rerank-gpu` compose profile serves the cross-encoder; when `ISIS_DEFAULT_RERANK_BASEURL` is set, the server seeds a Rerank endpoint once it answers, and new RecallDB scopes attach the tenant's first active Rerank endpoint. A rerank endpoint that fails is skipped for 30 seconds (`RerankCooldown`) so an unreachable reranker does not slow every search |
| 2. Re-tune hybrid fusion per model | **Done, with no per-model override needed.** A sweep of text weight (0.3, 0.5, 0.7) and RRF constant (20, 60) put both models at 0.5 and 20 (mean Hybrid nDCG@10 over four datasets: all-minilm 0.827 vs 0.826 at 60, nomic 0.815 vs 0.808). The generic RRF constant is now 20 (`HybridFusion.DefaultRrfK`); profiles and each search (`textWeight`, `rrfK`) can still override it |
| 3. Chunk size per model | **Done.** A nomic-embed-text sweep chose a 128-token cap (mean 0.826 at 128, 0.808 at 256, 0.806 at 384, 0.797 at 512), now in its profile. all-minilm keeps the generic 75% of budget, capped at 256. With its own chunk size, nomic ties all-minilm in Hybrid (0.826 vs 0.827) at twice the vector size, so all-minilm stays the default |
| 4. gpt-oss-20b as a prompted reranker | **Measured, adopt as opt-in.** The best reranker measured: isis-live 0.974, Atlas 0.915, SciFact 0.751, LongMemEval 0.959, against 0.925, 0.880, 0.715, and 0.931 for the cross-encoder, but at 6.6 to 10.2 s per search (p50). Its scores also separate answerable from unanswerable questions far better than any earlier signal (AUROC 0.991 on isis-live and 0.960 on Atlas, against 0.57 to 0.79) |
| 5. GPU-served reranking | **Done.** The `rerank-gpu` compose profile serves the same model on a GPU under the same network alias, so the seeded endpoint works with either |
| 7. Sub-queries for multi-part questions | **Built, measured, off by default.** `additionalQueries` and `decompose` on search, and chat decomposition (`retrieval.chatQueryDecomposition`). gemma3:4b decomposition fused at equal weight lowered every dataset (isis-live 0.879 → 0.844, Atlas 0.831 → 0.764, SciFact 0.682 → 0.666, LongMemEval 0.912 → 0.901, about 1 s per search) and chat accuracy (0.944 → 0.900). Chat decomposition now defaults to off; the search options remain for callers that supply their own sub-queries |

What round 6 taught:

- **The fusion constant was the one generic setting left untuned**, and a smaller constant helps both embedding models
  slightly. No model needed its own fusion weights, which keeps the profile list short.
- **Chunk size, not the model, was holding nomic back in Hybrid.** At 128 tokens it matches all-minilm; neither model
  beats the other enough to justify a change of default.
- **A large chat model is a strong reranker and a strong relevance judge**, at a latency only suitable for an opt-in
  high-precision mode or a final check before answering.
- **Adding queries at full weight hurts.** Decomposed parts displaced the memories the original question already
  ranked well. Any future multi-query work (decomposition or rewrite) must keep the original query dominant.

The current table replaces the round-5 table. Scores are value plus simplicity, each 1 to 10.

| Rank | Fix | Weak area | Value | Simplicity | Score |
|---|---|---|---|---|---|
| 1 | Opt-in model-judged relevance: document a prompted Rerank endpoint on a larger chat model as a high-precision mode, and choose a `minRerankScore` cutoff for it from the answerable and unanswerable score distributions | "Nothing relevant", and ranking when latency is not a concern | 6 | 7 | 13 |
| 2 | Conversation-aware query rewrite in chat: turn a follow-up ("and who owns that now?") into a standalone query from the session's earlier turns before retrieval, and search it alongside the original turn | Multi-turn chat follow-ups (not yet measured; needs a multi-turn dataset) | 6 | 6 | 12 |
| 3 | Weighted query rewrite: keep the original query dominant and fuse, at a lower weight (0.3 to 0.5), a model-drafted hypothetical answer on the vector leg and keyword expansion on the text leg; deterministic (temperature 0) and model-agnostic | Paraphrase and vocabulary mismatch without a reranker | 5 | 7 | 12 |
| 4 | RecallDB single-call hybrid search, per the RecallDB plan, then move Isis to it | Keyword latency, and a text leg that cannot see recency | 6 | 5 | 11 |
| 5 | Per-query weights in multi-query fusion, then re-test decomposition with the original query weighted above its parts | Multi-memory (Atlas multi 0.815, LongMemEval preference 0.785) | 5 | 5 | 10 |
| 6 | Abstention from a combined, calibrated signal (vector score, rerank score, and score gap) for deployments without a large reranking model | "Nothing relevant" | 6 | 4 | 10 |
| 7 | Stored vectors in RecallDB search results through the SDK | Diversity and similarity on whole memories | 4 | 4 | 8 |

Items 3 and 5 share their first step (a weight per query in `FuseQueryResults`), so they are cheapest done together.

### Query rewrite

**Weighted query rewrite.** Decomposition fused each part at the same weight as the original question, and it pulled
rankings the original query already had right down to the fragments' level. A rewrite must not do that. The original
query keeps full weight; the rewritten forms are fused at 0.3 to 0.5. Two forms target the two legs: a short
hypothetical answer (embedded for the vector leg, since answers look more like stored memories than questions do) and
keyword expansion (the terms a relevant memory would likely contain, for the text leg). The prompt is the same for
every model, runs at temperature 0, and a reply that cannot be used falls back to the original query. It reuses the
additional-query fusion path, with a per-query weight added. Measure it on paraphrase and lexical questions, with and
without the cross-encoder; the reranker already repairs most vocabulary mismatch at lower cost, so expect the gain
mainly on scopes without one. AssistantHub measured the replace-the-query form at +0.010 nDCG for 2.5 s per query,
which is the bar the weighted form has to beat.

**Conversation-aware rewrite.** A follow-up in a chat session ("and for staging?") retrieves poorly on its own. Before
retrieval, have the chat model rewrite the latest turn into a standalone query using the session's earlier turns, and
search both forms with the original at full weight. This is a different problem from vocabulary mismatch, and the
reranker does not solve it. It needs a small multi-turn dataset to measure.
