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
| 1 | Mark a memory as replacing another (a `supersedes` field on upsert); search demotes or hides the replaced memory | Superseded facts | 9 | 6 | 15 | Later |
| 2 | Use a stronger default embedding model (choose by benchmark); needs a re-embed | Paraphrase | 8 | 7 | 15 | Later |
| 3 | Recency boost in ranking: break close scores in favor of the newer memory | Superseded facts | 7 | 8 | 15 | Done (round 2) |
| 4 | Guide agents to update, not duplicate: default instructions tell agents to re-upsert the existing slug when a fact changes | Superseded facts | 6 | 9 | 15 | Done (round 2) |
| 5 | Raise chat's default retrieval depth (topK 5 → 8) | Multi-memory | 5 | 10 | 15 | Done (round 2) |
| 6 | Follow memory links: pull in memories the top hits link to with `[[slug]]` | Multi-memory | 7 | 7 | 14 | Later |
| 7 | Embed a header on each chunk (title and summary, not just body text) | Paraphrase | 6 | 8 | 14 | Done (round 2) |
| 8 | RecallDB: skip or cap the count query on ranked text and hybrid searches | Keyword latency | 6 | 8 | 14 | Already addressed upstream (see note) |
| 9 | Stricter chat prompt: answer only what memory states, cite every claim, otherwise say it isn't in memory | No "nothing relevant" signal | 5 | 9 | 14 | Done (round 2) |
| 10 | Return the fused score and each leg's raw score, and add a `minScore` filter | No "nothing relevant" signal | 5 | 8 | 13 | Done (round 2) |
| 11 | Rerank the top results with a cross-encoder (a new endpoint type) | Paraphrase, multi-memory, no "nothing relevant" signal | 8 | 4 | 12 | Later |
| 12 | Check for duplicates at write time: search for similar memories on upsert and flag or auto-link likely replacements | Superseded facts | 8 | 4 | 12 | Later |
| 13 | "Nothing relevant" cutoff on the reranker's score (needs #11) | No "nothing relevant" signal | 7 | 5 | 12 | Later |
| 14 | Diversify results (MMR) so near-duplicate chunks don't crowd out other memories | Multi-memory | 5 | 7 | 12 | Later |
| 15 | Use RecallDB's single-call hybrid search instead of Isis's own two-call fusion (needs an SDK release that exposes the hybrid options) | Keyword latency | 6 | 5 | 11 | Later |
| 16 | Split multi-part questions into sub-queries with the chat model | Multi-memory | 6 | 4 | 10 | Later |
| 17 | Query expansion (search with a model-drafted hypothetical answer) | Paraphrase | 5 | 5 | 10 | Later |
| 18 | Cache auth, scope and endpoint lookups (saves ~11 ms per search, but delays credential revocation) | Keyword latency | 4 | 6 | 10 | Later |

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

## Measured results

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

What this changes for the next round:

- **#1 (explicit supersession) moves up.** Recency got superseded facts from 40% to 55% ranked first, and the sweep
  shows no weight gets much further without hurting undated corpora.
- **The lexical regression from #7 needs attention.** For example, embed the header only on chunks after the first,
  or weight the text leg up for queries that look like identifiers.
- **#2 and #11 remain the biggest levers** for paraphrase (still 45% ranked first on Atlas) and for abstention.
