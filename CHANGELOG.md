# Changelog

All notable changes to NotDory are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0] - ALPHA (in progress)

### Added

- **Filesystem mirror for RecallDB scopes.** A RecallDb scope with `filesystemMirror: true` and a `targetPath` writes
  every memory to RecallDB and, concurrently, to an Open Knowledge Format bundle in a `.okf` directory under it (one
  markdown file per memory with YAML frontmatter, plus a generated `index.md`), so a project gets semantic search and a
  git-trackable copy of its memory in the repository. `targetPath` is meant to be the repository root: NotDory appends
  `.okf` itself (`MirroredMemoryStore.BundleDirectoryName`), so the bundle and its `index.md` never mix with, or index,
  the repository's own files. RecallDB stays the system of record and serves every search and chat; a write fails if
  either side fails; mirror writes to one directory are serialized so `index.md` stays complete. Turning the mirror on
  (or changing its `targetPath`) copies the scope's existing memories into the bundle, and deleting the scope leaves the
  files in place. Validation returns 400 for a mirror on a Filesystem scope, without a `targetPath`, or with a path the
  server cannot create. New `MirroredMemoryStore`, migration `2026-10-01-scope-filesystem-mirror` (Migration012), REST,
  MCP (`scope_create` and `scope_update` take `filesystemMirror` and `targetPath`), dashboard (scope form checkbox and
  scope detail), docs, and tests.
- **The OKF mirror is on by default and follows OKF v0.2.** A new RecallDb scope given a `targetPath` mirrors unless the
  request sends `filesystemMirror: false` (new server setting `storage.mirrorByDefault`, default true, env
  `NOTDORY_MIRROR_BY_DEFAULT`); NotDory never picks a directory itself, so a scope created without one starts
  unmirrored. `session_start` takes `path`, the repository root's absolute path (the Claude Code SessionStart hook,
  `notdory mcp install`, and the per-OS hook scripts now send it): a scope it creates mirrors to `<path>/.okf` when the
  server can see that directory and `.okf` is absent or empty, and otherwise its `notice` says why not and how to turn
  the mirror on. OKF prescribes no location; it recommends keeping a bundle in git, as a subdirectory of the repository
  it describes, and `.okf/` at the repository root is the directory community OKF tooling defaults to. The generated
  root `index.md` now conforms to OKF v0.2 section 8 (its only frontmatter is `okf_version: "0.2"`, with one
  `# <category>` section per category listing `* [title](path) - description`), and a memory replaced by a newer one is
  written with `status: deprecated` and `supersededBy` (its file is rewritten when supersession changes). A `targetPath`
  starting with `~` is rejected with 400, because the server does not expand it. The dashboard's new-scope form has the
  mirror checked by default.
- **Dashboard navigation consolidated into seven tabbed hubs.** The sidebar goes from 17 items in 7 groups to two
  sections: **Workspace** (Home; Memory: Scopes, Memories, Instructions; Recall: Search, Chat) and **Administration**
  (Models: Embedding, Inference; Access: Tenants, Users, Credentials; Monitoring: Requests, Operations, API Explorer;
  System: Settings, Agent Onboarding, Collections). The active tab is in the URL (`?tab=`), tabs support arrow-key
  navigation, admin gating is unchanged and applied per tab, and every old URL redirects to its hub and tab with the
  query string kept. The nav is defined once in `dashboard/src/config/navConfig.jsx`.
- **Home's memories-per-scope chart labels every bar.** Labels stay horizontal when they all fit and turn vertical when
  they don't, with the chart growing taller to fit them (vertical labels over 32 characters are shortened, with the full
  name on hover). The chart shows at most 32 scopes, those with the most memories, and says so when there are more.
- **Request History table fits the page.** The Path column is truncated with the full path on hover, Principal and Tenant
  are hidden by default (they can be shown from the column picker), and wide tables scroll inside their frame instead of
  widening the page.

- **Agents keep one scope per project and onboard it.** The default server instructions (and so every `session_start`
  protocol) now tell an agent that every project it does a meaningful amount of work on gets its own scope, and that a
  new, empty, or thin scope is onboarded: examine the project's structure and key details, describe the scope
  (`scope_update`), create categories with descriptions and instructions (`category_create`), and save memories
  (`memory_upsert`). `session_start` adds a `notice` saying so when the scope was just created or has no memories, and
  the `session_start`, `scope_create`, and `category_create` tool descriptions and the per-agent instruction docs say
  the same. Administrators who overrode the server instructions or those tool descriptions keep their text.
- **Agents audit memory when they save.** The default server instructions and the `memory_upsert` description now
  permit and expect an agent, each time it writes memories, to check the scope's categories (create or update any that
  are missing or unclear) and to check that memory is complete enough for a new agent to start from, adding, updating,
  superseding, or deleting memories until it is. No confirmation is required first.
- **Claude Code needs one install step, for every project.** `scripts/*/install-claude` registers NotDory at user scope
  and installs the SessionStart hook (new `notdory-claude-hook.ps1` / `notdory-claude-hook.sh`, idempotent, other hooks and
  settings kept, `remove-claude` undoes both). The hook sends the project's git remote and folder name, and session
  start resolves the scope by the remote's repository name, then the folder name, so a clone in a differently named
  folder still finds its memory, a new repository gets a scope named for it, and a bare folder never creates one
  (`remote` and `directory` on `POST`/`GET /v1.0/api/session` and the `session_start` tool).
- **From connect to using memory in one step.** NotDory now tells a connecting agent how to use it and gives it its
  context in one call:
  - **Server instructions** in the MCP `initialize` result (which harnesses place in the model's system prompt, the one
    channel that reaches the model even when tool descriptions are deferred): call `session_start` with the project
    name, search memory before answering or changing code, save decisions, facts, preferences, and corrections as you go.
  - **`session_start`** (MCP tool, `POST`/`GET /v1.0/api/session`): the project's scope, matched by name ignoring case
    and punctuation or created if missing, plus the protocol, categories, effective instructions, and recent memories.
    It replaces the `whoami`, `instructions`, `scope_enumerate`, `guide` sequence; `format=text` renders markdown.
  - **`tenantId` is optional on every MCP tool**, defaulting to the credential's tenant.
  - **Categories by name:** a memory upsert accepts a category name (matched ignoring case, created on first use) as
    well as a `cat_` id.
  - **Tool descriptions say when to use each tool** (`memory_search` before answering or changing code, `memory_upsert`
    after decisions and discoveries); `session_start` is listed first.
  - **Claude Code SessionStart hook:** `notdory mcp install` adds a hook that injects the session context before the first
    turn (`--no-session-hook` to skip, `--rest-url` for the REST API).
- **Editable agent onboarding.** Everything sent to the model on connect (the server instructions and every tool
  description) is editable by a system administrator in the dashboard (**Agent onboarding**) or through
  `GET`/`PUT /v1.0/api/agent-protocol` (also the `agent` section of the server settings). Edits apply without a
  restart: the MCP server re-reads them every 30 seconds, re-registers the tools in order, and notifies connected
  clients that the tool list changed. Default tool descriptions live in `AgentToolCatalog`.
- **Endpoint reasoning setting.** An inference endpoint's `reasoning` (`Default`, `Off`, `Low`, `Medium`, `High`) sets
  how much a reasoning model thinks on NotDory's calls to it: chat answers, query steps, and reranking, through PolyPrompt
  2.7.1's per-call reasoning option. `Default` (every existing endpoint, via migration
  `2026-09-27-endpoint-reasoning`) sends nothing, so behavior is unchanged. It makes thinking models practical as
  rerankers and query models: qwen3:8b timed out as a reranker with thinking on. Set it per endpoint because models
  honor different values (gpt-oss takes `Low` but ignores `Off`; qwen3 honors `Off`). Dashboard endpoint form, REST
  and MCP, docs, and tests; the benchmark harness takes `--rerank-reasoning`.
- **Full health check URLs.** An endpoint's `healthCheckUrl` can be a full http or https URL used as-is, as well as a
  path appended to the base URL, so an endpoint whose base URL points at one model behind a proxy (for example
  `http://proxy:8900/v1.0/api/gpt-oss-20b/`) can check the proxy itself (`http://proxy:8900/`). Other schemes are
  rejected with 400. The dashboard's endpoint form explains both forms.
- **Benchmark harness (`src/Test.Benchmark`, `benchmarks/`).** A black-box REST/MCP harness with `retrieval`, `chat`,
  `agent`, `load`, `stub`, `prepare`, and `compare` commands, an isolated pgvector + RecallDB stack, hand-labelled
  datasets (`notdory-live`, `atlas`), converters for BEIR and LongMemEval, and a CI regression gate. The first baseline
  is in `benchmarks/RESULTS.md`. Every retrieval report shows the dataset's published baselines
  (`benchmarks/baselines.json`) with the net difference, and a `history` command tabulates any set of rounds per
  question type with the net change and baselines.
- **Multi-query search.** `additionalQueries` (up to 4) are searched alongside `queryText` and the rankings fused;
  `decompose: true` has an inference endpoint split the question first, and the response lists the `queries` run
  (REST and MCP `memory_search`). Chat can decompose questions too; it is off by default (see per-scope models below).
- **Rerankers are inference endpoints.** The separate `Rerank` endpoint kind and its dashboard page are gone: every
  model that is not an embedding model is an inference endpoint, configured the same way, and a scope picks which one
  answers chat, runs query steps, and reranks. The API format decides what an endpoint can do: `Ollama`, `OpenAI`, and
  `VLlm` chat models can also rerank by prompt, and `Tei` and `Cohere` cross-encoders only rerank. The same chat model
  can now answer chat and rerank without being registered twice. Migration `2026-09-27-rerank-endpoints-are-inference`
  converts stored Rerank endpoints (keeping their `rep_` ids; `VLlm` rerankers, which called vLLM's Cohere-compatible
  API, become `Cohere`). `Rerank` is still accepted as a kind on input and stored as `Inference`. New scopes attach the
  tenant's first cross-encoder automatically, never a chat model, and scope validation rejects a cross-encoder as the
  chat or query model. `VLlm` endpoints used as rerankers now rerank by prompt; use `Cohere` for a vLLM-served
  cross-encoder.
- **PolyPrompt 2.7.0 for every model call.** Reranking now goes through PolyPrompt like chat and query steps: `Tei`
  and `Cohere` cross-encoders use its `RerankAsync` (TEI with truncation and 0 to 1 scores; Cohere through its v2
  rerank API, `/v2/rerank`, instead of `/v1/rerank`), and chat models are prompted through its chat API at temperature
  0, so `Gemini` models can rerank too. A shared `ModelClientFactory` gives every job the same authentication, retry,
  and timeout handling. Reranked benchmark scores are unchanged.
- **Per-scope models and query steps.** A scope names the model for each job, like an assistant's settings in
  AssistantHub: `inferenceEndpointId` (chat answers) and `queryEndpointId` (follow-up rewriting, splitting, and
  expansion), next to the existing embedding and rerank endpoints, plus `conversationRewrite`, `queryExpansion` (`Off`,
  `On`, or `Auto`), and `queryDecomposition` (migration `2026-09-27-scope-models`). Null means the server default, and a
  search's `expand` and `decompose` or a chat request's `inferenceEndpointId` override the scope for that call. A
  high-precision mode is a scope whose reranker is a large chat model with a `rerankMinScore` cutoff. REST, MCP
  (`scope_create`, `scope_update`), and the dashboard scope form expose every field; scope create and update reject a
  missing or wrong-kind endpoint. Server settings `retrieval.queryExpansion` (default `Auto`) and
  `retrieval.queryDecomposition` replace the chat-only `chatQueryExpansion` and `chatQueryDecomposition`.
- **Query expansion on by default for searches that are not reranked** (`queryExpansion: Auto`). Over two benchmark
  runs it raised nDCG@10 on SciFact (+0.037) and LongMemEval (+0.021) and was neutral on notdory-live and Atlas (the small
  model's drafts vary between runs by about 0.01); it adds a model call (about 2 s) per search and per chat question.
- **RecallDB single-call hybrid search.** When the RecallDB server reports the capabilities, a hybrid search is one
  request: RecallDB fuses both legs with NotDory's weights, RRF constant, and recency and collapses chunks to one hit per
  memory. Results are identical to the two-call path, which remains the fallback (`retrieval.serverSideHybrid`).
- **Weighted multi-query search.** Extra queries are fused by weighted reciprocal rank with the original query at 1.0:
  `additionalQueryWeight` weights `additionalQueries` and decomposed parts (server default
  `retrieval.additionalQueryWeight`, 1.0), and new `subQueries` carry their own `weight` and optional `mode`. The
  multi-query fusion constant is now a setting, `retrieval.queryFusionRrfK`, and defaults to 5 instead of 60: at 60 a
  single top hit from a 0.3-weight query outranked most of the original query's top ten, so weights had little effect.
  At 5, decomposition went from lowering every dataset to roughly neutral (mean nDCG@10 0.804 to 0.826 at weight 0.5).
- **Query expansion.** `expand: true` on search (REST and MCP) has the inference endpoint draft a short hypothetical
  answer, searched by vector, and keywords, searched as text, fused at `expansionWeight` (default 0.5,
  `retrieval.expansionWeight`). Over two runs it raised nDCG@10 on SciFact (+0.037) and LongMemEval (+0.021) and
  was neutral on notdory-live and Atlas; it costs a model call per search (about 2 s) and trails the cross-encoder on
  memory-style data. It runs automatically for searches that are not reranked (see
  per-scope models below).
- **Input validation.** Every request value and setting is checked where it enters (`InputGuard`): numbers clamp to a
  valid range and NaN or infinity falls back to the default; oversized text and lists are rejected with 400 naming the
  field; null lists become empty and undefined enum values fall back. Request bodies over `rest.maxRequestBodyBytes`
  (default 16 MB) are rejected with 413, and query-string values are limited to 2,048 characters. Settings files are
  clamped rather than rejected, so an out-of-range value never stops the server from starting, and a null settings
  section takes its defaults. The limits are listed in `docs/REST_API.md`.
- **SEARCH_PIPELINE.md** describes every stage of the write and read paths with its rationale, implementation, settings,
  and measured effect.
- **Follow-up questions in chat.** Chat accepts `history`, the earlier messages of a conversation (REST, the MCP
  `chat` tool, and the dashboard, which sends the last 6). The inference endpoint rewrites a follow-up into a
  standalone query, retrieval searches both the question and the rewrite, the answer prompt shows the recent
  conversation, and the response carries `standaloneQuestion` (`retrieval.chatConversationRewrite`, default true;
  `retrieval.chatHistoryTurns`, default 6). A benchmark dataset of follow-up questions, `notdory-live-followups`, and
  `history` support in the harness's `chat` command measure it.
- **Memory supersession.** An upsert can name the memories it replaces (`supersedes`, a list of slugs or ids). The
  server keeps `supersededBy` on each replaced memory (migration `2026-09-24-memory-supersession`). Search demotes a
  replaced memory to directly after its replacement by default (`superseded`: `Demote`, `Hide`, or `Include`),
  adds the replacement when the search missed it, follows replacement chains, and marks hits with `supersededBy`.
  Chat labels outdated memories for the model.
- **Rerank endpoints.** A new endpoint kind, `Rerank` (`rep_` ids), with the `Tei` (Hugging Face Text Embeddings
  Inference) and `Cohere` (Cohere-compatible, also vLLM) API formats. A scope can name a `rerankEndpointId`, how many
  `rerankCandidates` to score (default 10), and a `rerankMinScore` cutoff (migration `2026-09-24-scope-rerank`).
  Searches in the scope are reranked by default (`rerank: false` opts out) and hits carry `rerankScore`.
- **Relevance cutoff.** `minRerankScore` on a search (or the scope's `rerankMinScore`) drops reranked hits below
  it, so a question with no relevant memory returns no hits. When that happens in chat, the model is grounded on no
  memories and says the answer is not in memory, instead of receiving the scope overview.
- **Link expansion.** `linkExpansion` (0 to 10) adds memories linked from the results, through `links` or
  `[[slug]]` references in the body, directly after the result that links to them (`linkedFrom`). Chat follows up
  to 2 links by default (`retrieval.chatLinkExpansion`).
- **Result diversity.** `diversity` (0 to 1, default 0) reorders results by maximal marginal relevance over word
  overlap, so near-duplicate results give way to other relevant memories.
- **Similar memories on upsert.** On scopes with semantic search, the upsert response lists existing memories whose
  embedding is close to the new one (`similarMemories`, threshold `retrieval.duplicateSimilarityThreshold`, default
  0.85), so a writer can reuse the slug or supersede the old memory.
- **Lookup cache.** Credentials, users, scopes, and model endpoints read on every request are cached for a few
  seconds (`cache.enabled`, `cache.ttlSeconds`, default 10). Writes through the node invalidate the affected entries
  immediately; the time to live bounds how long a change made on another node goes unnoticed.
- **MCP.** `memory_upsert` accepts `links` and `supersedes`; `memory_search` accepts `superseded`, `linkExpansion`,
  `diversity`, `rerank`, and `minRerankScore`; `scope_create` accepts the chunking and rerank settings. The
  dashboard has a Rerank Endpoints page and rerank settings on the scope form.

### Changed

- **Dependency updates (2026-10-03).** PolyPrompt 2.7.1 -> 3.1.0, Microsoft.Data.SqlClient 7.1.0 -> 7.1.1, Watson
  7.2.0 -> 7.2.2, Voltaic 2.1.13 -> 2.2.1, Touchstone.Core and Touchstone.Cli 0.1.12 -> 0.2.0. PolyPrompt 3 splits each
  provider client into one client per capability, so `ModelClientFactory.Create` now returns the provider's completion
  client (`OllamaCompletionClient`, `OpenAiCompletionClient`, `GeminiCompletionClient`, `CohereCompletionClient`) and
  throws `NotSupportedException` for TEI, and the new `ModelClientFactory.CreateRerank` returns the cross-encoder client
  (`TeiRerankClient`, `CohereRerankClient`) that `RerankService` uses for rerank-only formats. `ChatCompletionOptions`
  is now `CompletionOptions`. Endpoint auth, reasoning, timeouts, and wire formats are unchanged. New test
  `model-client-factory` covers the client chosen for each API format; the request-history failure test now waits for
  the post-routing write, as the 401 case already did.
- **Verbex removed.** The never-wired Verbex store provider is gone: the `Verbex` value of `storeProvider`, the
  `VerbexMemoryStore`, the `verbex` settings section, and its documentation. Creating a scope with `storeProvider: "Verbex"`
  is a 400 (an unknown provider). Store providers are now RecallDb and Filesystem.

- **Renamed from Isis to NotDory** (breaking). The new name is used everywhere: projects and namespaces
  (`NotDory.Core`, `NotDory.Server`, `NotDory.McpServer`, `NotDory.sln`), the settings file (`notdory.json`),
  environment variables (`NOTDORY_*`), Docker images (`jchristn77/notdory-server`, `-mcp`, `-dashboard`), compose
  services, the Postgres database (`notdory`), the MCP server key (`notdory`), the Claude Code hook
  (`notdory-claude-hook.sh` / `.ps1`), Prometheus metrics (`notdory_*`), Grafana dashboards, the local-dev defaults
  (`notdorydefaultkey`, `admin@notdory.local`), the Postman collection, and the repository URL
  (`github.com/jchristn/notdory`). There are no aliases for the old names. To move an existing install, rename
  `isis.json` and `ISIS_*` variables, recreate the stack (its Postgres volume holds a database named `isis`), and
  re-run `scripts/*/remove-*` from the previous checkout and then `scripts/*/install-*` so agent configs point at
  `notdory`.
- **Retrieval improvements** (see `archive/RETRIEVAL_IMPROVEMENTS.md`):
  - Hybrid scores are now fused and normalized to 0..1. Hits carry `vectorScore`, `textScore`, `vectorRank`, and
    `textRank`, and search accepts a `minScore` threshold (REST and MCP).
  - A recency signal (`recencyWeight`, default 0.1, 0 disables) favors the newer of two similar memories.
  - Each chunk is embedded with its memory's title and summary.
  - Chat retrieves 8 memories by default instead of 5, and its prompt now forbids unstated facts and requires a
    citation for every claim.
  - Default instructions tell agents to update a changed fact in place rather than add a duplicate.
- **Voltaic 1.1.0.** The MCP server moves from Voltaic 0.6.1 to 1.1.0, which fixes Claude Code 2.1.x seeing no NotDory
  tools (it uses `server/discover` and the stateless `2026-07-28` revision). Regression cases were added to the MCP
  suite.
- **Voltaic 2.0.0.** The MCP server moves to Voltaic 2.0.0. `tools/list` now returns only the 32 NotDory tools: the
  Voltaic demo tools `ping`, `echo`, `getTime`, and `getSessions` are gone (`getSessions` disclosed every caller's
  `Mcp-Session-Id`). The protocol `ping` returns `{}` (`{"resultType":"complete"}` under `2026-07-28`) instead of
  `"pong"`, and still needs no credentials. A tool can no longer be called as a bare JSON-RPC method; use
  `tools/call`. MCP clients that called the removed tools or read `"pong"` need updating. New MCP suite cases cover
  the exact tool list, the `ping` result on both revisions, rejection of the removed tools and of bare tool calls,
  and unauthenticated `tools/call`.
- **Voltaic 2.1.13.** The MCP server follows the current MCP specification more strictly: `ping` now requires the
  credential like every other request (401 without one), needs an initialized session on the session transport, and
  is not a method of the stateless `2026-07-28` revision (`-32601`); use `GET /` to probe connectivity. Browser
  requests from foreign origins are refused (403), and a server bound to `localhost` accepts loopback clients only (the
  Docker image binds `*`). MCP clients that pinged without a credential or before `initialize` need updating.
- **Dependencies.** Microsoft.Data.SqlClient 7.1.0, Microsoft.Data.Sqlite.Core 10.0.12, MySqlConnector 2.6.2,
  Watson 7.2.0, and OpenTelemetry 1.19.x.
- **RecallDb.Sdk 0.2.3.** A category-filtered vector search asks RecallDB for `EfSearch` 1000 when the server reports
  `search.vector.ef-search`: the filter applies to the vector index's nearest rows, and RecallDB measured a selective
  filter returning 0 of 10 hits at the default scan size and 9 of 10 at 1000. RecallDB now answers 409 for an existing
  document key; an upsert that meets one (another writer recreated the memory between NotDory's delete and create)
  clears the memory's documents and writes once more.
- **RecallDb.Sdk 0.2.2.** Exposes RecallDB's single-call hybrid search, per-leg ranks, stored vectors on request, and
  a capabilities list, which NotDory does not use yet. Behavior changes that reach NotDory: existence checks throw on any
  status other than 200 or 404, so a failing or unauthorized RecallDB is reported as an error instead of "missing",
  and every request times out after 100 seconds. Retrieval results are unchanged (notdory-live and Atlas Hybrid
  nDCG@10 0.877 and 0.835, matching round 6).
- **Chat grounds on the whole best-matching chunk** instead of a 240-character snippet. Answer accuracy on the
  notdory-live benchmark rose from 0.68 to 0.96.
- **Hybrid search runs its vector and text legs in parallel**, and a multi-chunk memory's chunks are embedded
  concurrently (`retrieval.embeddingParallelism`, default 4).
- **TextChunker 0.3.1.** Span-based chunking with token counts that match the embedding runtime. NotDory's workarounds
  for the 0.2.x bugs (surrogate-safe stand-in text, dropping redundant tail chunks) are removed, and its token
  margin drops from 4% to 1% (`MemoryChunker.TokenizerMarginFraction`). Chunk boundaries change, so re-ingest.
- **Default chunk size.** When a scope does not set `chunkMaxTokens`, chunks are 75% of the model's budget, at most
  256 tokens (`MemoryChunker.DefaultChunkFraction`, `DefaultChunkMaxTokens`). On the benchmarks this took Atlas from
  0.802 to 0.831 and LongMemEval from 0.895 to 0.912 Hybrid nDCG@10.
- **Model endpoint retries.** Embedding, rerank, and inference calls retry 429, 502, and 503 with backoff
  (`TransientRetryHandler`); an endpoint still unavailable afterwards is reported as 503 instead of 400.
- **Embedding task prefixes.** Models trained with them (nomic-embed-text, e5, bge, mxbai, snowflake-arctic-embed)
  get their document and query prefixes. Prefixes now live in embedding model profiles (below).
- **Chat-model reranking.** A Rerank endpoint can use the `Ollama` or `OpenAI` format; NotDory prompts the model once per
  search to rate every candidate. A cross-encoder remains the recommended reranker.
- **Rerank candidates default to 10** (was 20): equal quality on the benchmarks at about 40% less latency.
- **Reranker in the reference stack.** `docker/compose.yaml` and the benchmark stack gain optional `rerank` (CPU) and
  `rerank-gpu` (NVIDIA) profiles serving ms-marco-MiniLM-L-6-v2.
- **RecallDB clients are shared per endpoint** instead of one undisposed `HttpClient` per request.
- **Embedding model profiles** (`EmbeddingModelProfiles`, replacing `EmbeddingPrefixRegistry`). Every model uses
  generic defaults; a profile overrides only what a benchmark showed a known model needs: task prefixes, hybrid text
  weight, RRF constant, chunk fraction, and chunk cap. nomic-embed-text chunks are capped at 128 tokens.
- **Hybrid fusion's RRF constant defaults to 20** (was 60; `HybridFusion.DefaultRrfK`). A sweep on four datasets
  favored it for both all-minilm and nomic-embed-text. Search accepts `textWeight` and `rrfK` to override the
  profile or default per query.
- **Reranker on by default in the reference stack.** With `NOTDORY_DEFAULT_RERANK_BASEURL` set (as `docker/compose.yaml`
  does), the server seeds a Rerank endpoint once the reranker answers, and new RecallDB scopes attach the tenant's
  first active Rerank endpoint. A rerank endpoint that fails is skipped for 30 seconds (`RerankCooldown`), returning
  retrieval order with a notice.

### Fixed

- **The Windows Claude Code installer stopped after its first `claude` command.** `claude` is itself a batch file
  (`claude.cmd`), and a batch script that runs another without `call` never returns, so `install-claude.bat` ended
  inside `claude mcp remove` (exit 1) and neither registered NotDory nor installed the hook. Both Claude scripts now use
  `call`.
- **Query-string values are URL-decoded.** Watson returns them percent-encoded, so an encoded value (a git remote URL,
  a name with a space) arrived as `https%3A%2F%2F...` and matched nothing. `RouteHelpers.Query` now decodes them.
- **The seeded tenant instructions contradicted session start.** The default "Start here" and "Tools" instructions
  told agents that every tool needs a tenantId and to begin with whoami, instructions, scope_enumerate, and guide, and
  session_start returned them next to a protocol saying the opposite. New tenants now get text that matches session
  start (`DefaultInstructionText`), and migration `2026-09-28-session-start-instructions` updates existing tenants'
  copies that still hold any text NotDory has seeded (five historical versions); copies a tenant edited are left alone.
- **Failures while authenticating are recorded and mapped.** An exception thrown during authentication (for example a
  database error in the credential lookup) skipped post-routing, so the request answered 500 and left no row in request
  history. That is how the deployment's 20 minutes of 500s on access-key requests (2026-09-27, 08:14 to 08:35 UTC)
  became impossible to diagnose from the API. The authentication hook now catches the failure, answers it with the same
  status mapping as every other failure (a transient database error is a 503), and records it. Failed requests' history
  rows carry the exception type and message in an internal `x-notdory-exception` entry, never returned to the caller.
- **One bad rerank reply no longer switches reranking off.** A chat reranker's unusable reply (gpt-oss:20b at low effort
  sometimes returned 9 scores for 10 passages) started the 30-second endpoint cooldown meant for outages; since a
  search that skips the reranker takes about 60 ms, that left 240 of 300 SciFact queries unreranked. Unusable replies
  now raise `RerankReplyException`, fall back for that query only, and start no cooldown. The benchmark harness now
  reports how many queries were reranked and the server's notices.
- **A busy database answers 503, not 500.** A transient database error (a shared Postgres at its connection limit,
  a pool that waited past its timeout, a restart) now returns a retryable 503 `ServiceUnavailable` instead of a 500
  `InternalError`; the mapping moved to `ErrorClassifier`, with tests.
- **Request history and operation events are indexed by time.** The admin listings across tenants and the retention
  sweep scanned and sorted the whole table, the largest in the database once bodies are captured; migration
  `2026-09-27-history-created-indexes` adds `createdutc` indexes to both tables.
- **The Docker stack's Postgres allows 300 connections.** NotDory and RecallDB share it and each pools up to 100
  connections, so the default of 100 left no headroom during large ingests.
- **Crashes and runaway work on bad input:** a null entry in `subQueries` returned 500; a huge `tokenBudget` overflowed
  while trimming snippets; a null memory `metadata` threw partway through an upsert; a null settings section saved
  through the settings route broke the next start; a retention sweep interval over about 24 days stopped the retention
  loop; batch requests, chat history, and memory tags, links, and metadata had no size limits.
- **User delete orphaned records.** Deleting a user removed only the first 1,000 of the tenant's credentials, sessions,
  and permissions it scanned; it now pages through all of them.
- **Endpoint base URLs are validated** as absolute http or https URLs on create, update, and batch create (batch create
  previously skipped even the non-empty check).
- **Verbex scopes are rejected at creation** (400) instead of being created and failing on first use, and agent-facing
  text (MCP tool descriptions, default instructions, error messages) no longer recommends Verbex.

- **`endpoint_create` and `endpoint_update` MCP schemas** now declare `tenantId` and `endpointId`, which they
  required but did not list as properties.
- **`scope_update` over MCP no longer clears scope settings.** It sent only the name and description to a
  full-replace `PUT`, which dropped the embedding endpoint and chunking settings. It now reads the scope and changes
  only the fields passed.
- **Concurrent scope provisioning across a tenant.** Collection creation is serialized per tenant, and a failed
  create adopts an existing collection for the scope instead of retrying forever. It had caused 183 failed
  LongMemEval ingests when 8 scopes were provisioned in parallel.
- **Invalid Unicode and emoji.** Unpaired surrogates are replaced with U+FFFD on upsert. Chunking no longer splits
  a surrogate pair (TextChunker could, which failed the whole memory).
- **Redundant tail chunks.** Chunks wholly contained in the previous chunk are dropped (about 9% fewer chunks on
  long chat sessions, with nothing lost).
- **`memory_search` category filter** accepts a category name or `cat_` id (by name it previously matched nothing).
  An unknown category or an empty `queryText` now returns 400.
- **Concurrent first writes to a new scope** no longer race to provision the RecallDB tenant and collection.
- **Concurrent upserts of the same memory** are serialized, so there are no more duplicate-key 500s.
- **Embedding overflow on technical and accented text.** Chunks keep a 4% margin under an auto-resolved token budget,
  and a context-length rejection re-chunks at 0.75, 0.5, and 0.3 of the budget.
- **Unhandled exceptions return JSON** (400/501/503/500) through a server-wide exception route, instead of Watson's
  HTML error page.

- **Chunk document keys are now URL-safe (fixes orphaned chunks on delete).** Multi-chunk memories were
  keyed `{memoryId}#{ordinal}`, but the RecallDB SDK places the document key into the request path without
  URL-encoding — so the `#` (a URL fragment delimiter) truncated the delete/exists path and
  `DeleteByParentAsync` never matched a chunk, leaving orphaned chunk documents behind (still returned by
  search after the memory's index row was gone; also on category-cascade delete and on re-upsert of a
  multi-chunk memory, since upsert clears prior chunks the same way). Scope delete was unaffected (it drops
  the whole collection) and single-chunk memories were fine (key = memory id, no `#`). Fixed by composing the
  chunk key via `ChunkDocumentKey` with a URL-safe `-c` separator (all RFC 3986 unreserved characters); added
  a regression test asserting chunk keys need no URL escaping. Recommended upstream: `RecallDbClient` should
  URL-encode document keys in its path building. (Requires a redeploy; existing `#`-keyed orphans are only
  removable by dropping the scope.)

- **Embedding chunk budget fits the real model context (all-minilm).** The chunker's tokenizer library
  previously resolved `all-minilm` to the 512-token BERT architecture ceiling, but the real all-MiniLM-L6-v2
  caps at 256 — so oversized memories were embedded whole or in 512-token chunks and rejected with "input
  length exceeds the context length." Fixed by upgrading **TextChunker to 0.2.2**, which pre-configures the
  real per-model budgets (all-minilm 256, paraphrase-MiniLM 128, all-mpnet 384, mxbai/nomic corrected to the
  BERT tokenizer instead of tiktoken) and reserves the WordPiece `[CLS]`/`[SEP]` framing tokens in the
  effective budget (all-minilm resolves to 254 usable, `ProfileSource=KnownModel`). `MemoryChunker` keeps a
  shortfall-aware special-token reserve as a backstop — a no-op when the profile already reserves, and it
  never double-counts. No per-endpoint `MaxInputTokens` is needed for models the library knows; set one only
  to pin a model it doesn't. (Requires a redeploy; the deployed image must include TextChunker 0.2.2.)

- **Tenant deletion now tears down the external RecallDB tenant.** A tenant nuke already dropped every
  scope's collection (and filesystem content) and all tenant-scoped DB records, but the RecallDB tenant
  that NotDory provisions on first use was left behind as an empty orphan. `IMemoryStore` gained a
  best-effort `DeleteTenantAsync` (RecallDB drops the tenant; filesystem/Verbex no-op), which the tenant
  cascade invokes once after its scopes are gone. Scope deletion is unchanged (it must not drop the
  shared tenant). Added store-level no-op tests and a tenant-cascade test asserting the teardown is issued.

### Added

- **Native chunking of oversized memories.** A memory whose body exceeds the embedding model's token
  budget is now split into ordinal chunks that each fit the budget, embedded independently, and stored
  as sibling documents that share the parent memory's identity (`DocumentKey` `{memoryId}#{ordinal}`,
  `DocumentId` = slug, `parentKey` tag = memory id, `Position` = ordinal); a small memory still stores
  as a single document keyed by its id, unchanged. Retrieval fuses the vector and full-text rankings by
  reciprocal rank at the chunk level, then rolls chunks up to a single best-scoring hit per memory, so a
  long memory that matches only in its tail is no longer penalized by a truncated embedding. Chunking is
  configured per scope — `ChunkingMode` (`OnOverflow` default / `Always` / `Off`), `ChunkStrategy`
  (`FixedTokenCount` default), `ChunkMaxTokens` (0 = use the model budget), and `ChunkOverlapTokens`
  (64) — plus an optional per-endpoint `MaxInputTokens` override. Token counting and budget resolution
  are performed locally by the `TextChunker` library with no extra model call (for example `all-minilm`
  resolves to a 512-token BERT WordPiece budget). Added a `MemoryChunk` model, a `MemoryChunker` helper,
  an `IMemoryStore.UpsertAsync` that takes the chunk set, delete-by-parent cleanup on update and delete,
  and a six-case chunker test suite.

- **Versioned schema migrations.** Schema changes the create-only DDL cannot express now run through an
  ordered, provider-neutral migration framework (`ISchemaMigration` + `MigrationRunner`) that records
  applied migrations by name in a `schemamigrations` table and skips those already applied. Tables are
  created, then migrations run, then indices are created, so an index never references a column a pending
  migration has yet to add. Ships the base-URL/auth, scoped-instruction, scope chunk-config, and
  endpoint `MaxInputTokens` migrations, each data-preserving and idempotent on an already-current schema.

- **MCP tool surface brought to parity with the REST API.** The MCP server now exposes the full
  tenant-scoped resource surface (32 tools): scope/category/memory/endpoint/collection/instruction
  CRUD, endpoint health, and chat-with-memory — previously only enumerate/read + memory writes were
  available (notably, endpoint management was missing). Each tool proxies its REST route and accepts
  the same body fields. Admin-only surfaces (tenant/user/credential management, settings, session
  login, raw observability feeds) remain REST/dashboard-only by design. Added a `tools/list` parity
  test plus endpoint- and scope-CRUD MCP round-trip tests, and updated `docs/MCP_API.md`.

- **Scope-scoped instructions with a tenant-global fallback.** Instructions can now be attached to a
  specific scope in addition to the tenant-global set. Each scope instruction has a merge mode —
  `Append` (add a new instruction), `Replace` (override a same-named global's content in place), or
  `Hide` (suppress a same-named global) — matched to the global set by name. A new
  `GET …/scopes/{scopeId}/effective-instructions` route returns the merged, source-annotated result,
  and the `notdory_instructions` MCP tool takes an optional `scopeId` to return a scope's effective set.
  Implemented with a nullable `scopeId` (+ `mergeMode`) on the instruction model/table across all four
  DB providers, a pure `InstructionResolver`, scope-delete + tenant-delete cascades, a data-preserving
  migration (existing instructions become the tenant-global set), and a dashboard scope selector with a
  live "effective instructions" preview.

- **Model endpoints use a base URL with a generic auth model.** Embedding and inference endpoints
  are now addressed by a single `baseUrl` (a full URL onto which the API-format path is appended)
  instead of `hostname`/`port`/`useSsl`, so gateway deployments like **Conductor** — which expose a
  distinct base URL per model — are first-class. Outbound authentication is now configurable via
  `authType`: `None`, `BearerToken`, `ApiKeyHeader` (operator-named header), `QueryParam`
  (operator-named query parameter, e.g. Gemini's `key`), `BasicAuth`, and `AccessKeySecret`
  (access key + secret sent as two operator-named headers). NotDory applies auth itself through a
  shared `EndpointAuthenticator` (used by the embedding client, the inference client via a
  delegating handler around PolyPrompt, and the health prober), independent of API format. Bumped
  the **PolyPrompt** dependency 2.2.1 → 2.6.0. The `model_endpoints` schema changed accordingly
  across all four database providers; a data-preserving migration recomposes `baseUrl` from any
  existing `hostname`/`port`/`useSsl` rows and derives the typed auth from the former single
  `apiKey`. Dashboard endpoint editor, REST body, docs, and tests updated to match.

- **All agent installers are access-key-only.** All agent installers (Claude, Codex, Cursor,
  Gemini, Mux) now authenticate with the credential access key only; none send `x-secret-key`.
  The MCP installer dropped its `--secret-key` flag.
- **MCP access-key bearer authentication (single-header MCP clients).** The MCP server now
  authenticates a caller by the credential **access key**, presented either as
  `Authorization: Bearer <accessKey>` or in the `x-access-key` header; the `x-secret-key` header
  is now **optional** and validated only when present. The access key authenticates on its own —
  it is public and transferable, so it is a **capability token** to scope least-privilege — and a
  request with no access key is rejected with `401`. This makes NotDory reachable from MCP clients
  such as **Mux** that can send only a single auth header. `scripts/*/install-mux.*` now writes
  Mux's native bearer schema (a `servers[]` entry with `auth: { type: "bearer", bearerToken:
  <accessKey> }` and no secret), and every `install-<agent>` script accepts the access key as an
  optional first positional CLI argument (arg #1 > `NOTDORY_ACCESS_KEY` > default). Claude Code,
  Codex, Cursor, and Gemini send the access key in the `x-access-key` header; none send a secret.
- **Tenant auto-provisioning.** `POST /v1.0/api/tenants` (system administrator) now stands up a
  complete, ready-to-use environment in one call: the tenant plus an auto-generated tenant-admin
  user, a credential for that user, and a default instruction set. The response returns
  `{ tenant, admin: { userId, email, password }, credential: { credentialId, accessKey, secretKey } }`
  where the admin `password` and the `secretKey` are shown **once** and never again.
- **Cascading deletes across the backend.** Deleting a parent now removes all of its children,
  including external memory-store content: `DELETE /v1.0/api/tenants/{tenantId}` — the **"Nuke
  tenant"** operation — cascades to all of the tenant's users, credentials, scopes, categories,
  memories, instructions, endpoints, and sessions, and is refused with **409** for protected
  tenants (e.g. `ten_default`); deleting a scope cascades to its categories + memories and drops
  the store collection/files; deleting a category cascades to its memories; deleting a user
  cascades to that user's credentials, sessions, and permissions. The dashboard exposes **Nuke
  tenant** as a type-the-tenant-id confirmation action.
- **Batch retrieval / creation / deletion APIs (data layer + REST).** Every entity's data layer
  gained `ReadMany` / `CreateMany` / `DeleteMany`, surfaced through uniform REST batch endpoints
  (all POST): get/delete take `{ "ids": [...] }`, create takes `{ "items": [...] }`, and responses
  are `{ "objects": [...] }` (get/create) or `{ "deleted": <int> }` (delete). Instructions,
  Endpoints, and Memories expose the full trio (`…/batch-get`, `…/batch`, `…/batch-delete`;
  Memories `batch` is an upsert and `batch-delete` cleans store content); Scopes, Categories,
  Users, and Credentials expose `batch-get` + `batch-delete` (with the same cascade rules as their
  single-resource deletes). The tenant nuke is implemented as batch deletes over enumerated child
  ids.
- **Default instruction set per tenant.** A default instruction set is seeded for the first-run
  default tenant and provisioned for every newly created tenant.
- **Tenant-scoped Instructions — tenant-wide standing guidance surfaced to agents.** New resource
  with REST routes `GET/POST /v1.0/api/tenants/{tenantId}/instructions` and
  `GET/PUT/DELETE /v1.0/api/tenants/{tenantId}/instructions/{instructionId}` (reads open to the
  tenant; writes require `IsAdmin` / `IsTenantAdmin`), body `{ name, content, position, active }`,
  returned in ascending `position` order. Surfaced through the new MCP tool `instructions`
  (get the tenant's standing instructions) and a dashboard Instructions management view.
- **MCP `scope_create`** — create a memory scope for a project when none exists (params
  `tenantId`, `name`, optional `description` / `storeProvider` / `embeddingEndpointId` /
  `dimensionality` / `filesystemLayout` / `targetPath`), bringing the MCP tool count to twelve.
  MCP auth (`x-access-key` + `x-secret-key`, dev defaults `notdorydefaultkey` / `notdorydefaultsecret`)
  is now documented in the `whoami` tool description.
- **Server settings management (system administrator only).** `GET /v1.0/api/settings` returns
  `{ settings, settingsFile, liveSections }`; `PUT /v1.0/api/settings` persists a full settings body
  to the settings file (request-history changes apply immediately, other sections require a restart,
  signalled by `restartRequired: true`); `POST /v1.0/api/settings/restart` exits the node so Docker
  (`restart: unless-stopped`) relaunches it with the saved settings. Exposed in the dashboard with a
  **Restart Server** action.
- **Default seeded model endpoints.** On first boot NotDory seeds, for the default tenant when none
  exist, a default embedding endpoint (Ollama, model `all-minilm`, `http://localhost:11434`) and a
  default inference endpoint (Ollama, model `gemma3:4b`, `http://localhost:11434`).
- **Dashboard enhancements:** Request History page wired to `/v1.0/api/requests`; a full-width,
  searchable API Explorer operation picker with sample request bodies; Users, Credentials, and
  Instructions management views; row-click-to-edit plus Duplicate actions; an endpoint health-details
  modal; and Chat tenant / scope / inference-endpoint selectors.
- **Authentication overhaul — email/password sessions + two-key credentials.** Removed the admin
  `x-api-key` bootstrap scheme entirely. There are now exactly two mechanisms: (1) **email + password
  → session token** for interactive users and the dashboard — `POST /v1.0/api/tenants-for-email`
  lists the tenants an email belongs to, `POST /v1.0/api/token` issues a bearer token (sent as
  `Authorization: Bearer <token>` or `x-token`), `GET /v1.0/api/whoami` resolves the principal, and
  `DELETE /v1.0/api/token` revokes the session (logout); and (2) **credential access key** for
  automation, MCP, and agents — the access key authenticates on its own (sent as a bearer token or
  in `x-access-key`); the `x-secret-key` header is optional and validated only when present.
  Administrative power
  comes only from the user record's `IsAdmin` (system-wide) or `IsTenantAdmin` (tenant-wide) flags —
  there is no admin key or admin principal. Added user and credential management REST endpoints and
  dashboard views. First-boot seeding now creates a bootstrap admin user (`admin@notdory.local` /
  `notdoryadmin`, env `NOTDORY_AUTH_SEED_ADMIN_EMAIL` / `NOTDORY_AUTH_SEED_ADMIN_PASSWORD`) in tenant
  `ten_default` and a default credential (`notdorydefaultkey` / `notdorydefaultsecret`, env
  `NOTDORY_AUTH_DEFAULT_ACCESS_KEY` / `NOTDORY_AUTH_DEFAULT_SECRET_KEY`). The MCP installer takes an
  `--access-key` flag and writes the `x-access-key` header; a request with no access key is rejected
  with `401`.
- Initial repository scaffolding: `README.md` (with the backing-store capability matrix and alpha
  warning), `LICENSE.md` (MIT), `.gitignore`, `.dockerignore`, `notdory.json`, and Pneuma-style build
  scripts (`build.bat`, `test.bat`, `build-server.bat`, `build-mcp.bat`, `build-dashboard.bat`,
  `build-all.bat`).
- Product plan (`docs/NOTDORY_PLAN.md`), including the "Chat with Memory" surface and RecallDB
  pass-through collection management.
- `NotDory.Core` (builds clean; runtime-verified end to end):
  - Constants, PrettyId identifier generation, and domain enums.
  - Domain models: tenants, users, credentials, auth sessions, scopes, categories, memories, memory
    links (plus enumeration query/result types).
  - Provider-neutral database abstraction (`DatabaseDriverBase`, `DatabaseDriverFactory`) with a
    complete **SQLite** provider for tenants, users, credentials, sessions, scopes, categories, and
    the memory index.
  - Pluggable `IMemoryStore` seam with a working **filesystem** provider (single-file and hierarchy
    layouts, keyword search), and capability-accurate RecallDB/Verbex providers (integration wired in
    a later phase).

- `NotDory.Server` (REST, Watson 7.1 — runs and is tested end to end):
  - Watson host with the `AuthenticateRequest` hook, CORS preflight/post-routing, and OpenAPI
    (`/openapi.json` + Swagger).
  - Authentication (email/password session tokens for users and the dashboard; per-tenant credential
    `x-access-key` + `x-secret-key` for automation), authorization from the user's `IsAdmin` /
    `IsTenantAdmin` flags, and first-boot seeding of a default tenant, admin user, and access/secret
    credential.
  - Route registrars: health, server info, tenants, scopes, categories, memories (create/upsert,
    read, delete, list, **search**), and the agent **guide**. Scope updates preserve the store
    provider and embedding dimension (no silent model swaps).
  - `MemoryService` ties the memory index to the scope's store; memory writes are idempotent by
    `(scope, category, slug)`.
- `NotDory.McpServer` (Voltaic 0.6.1, standalone — runs and is tested):
  - Streamable-HTTP MCP transport that speaks the MCP `initialize` handshake and hosts 10 agent tools
    (`whoami`, `scope_enumerate`, `guide`, `category_enumerate`/`_create`,
    `memory_enumerate`/`_read`/`_upsert`/`_search`/`_delete`).
  - Authenticates the caller from transport headers (`x-access-key` + `x-secret-key`) and proxies each
    tool to the NotDory REST API over loopback, forwarding the caller's credentials so REST performs the
    authoritative auth and tenant scoping.
- Model endpoints + health checking:
  - `ModelEndpoint` (embedding/inference) with persistence (SQLite `model_endpoints` table,
    `IModelEndpointMethods`), tenant-scoped CRUD REST routes under `/v1.0/api/tenants/{id}/endpoints`,
    and kind-correct ids (`eep_` / `iep_`).
  - `HealthCheckService` that probes endpoints and **deduplicates by method + normalized URL + hashed
    auth**, applying one probe result to all endpoints sharing a target, with healthy/unhealthy
    threshold hysteresis. Live probe route at `/v1.0/api/tenants/{id}/endpoint-health`.
- Embedding + inference + chat-with-memory:
  - `EmbeddingService` and `InferenceService` (`NotDory.Core.Recall`) that call configured model endpoints
    in OpenAI-compatible and Ollama formats.
  - `MemoryChatService` — **Chat with Memory** RAG: retrieves the top memories from a scope, builds a
    grounded prompt, calls the inference endpoint, and returns a synthesized answer with **citations**.
  - REST chat route `POST /v1.0/api/tenants/{id}/scopes/{sid}/chat` (auto-selects the tenant's inference
    endpoint or takes an explicit one; clear 400 when none is configured).
- **RecallDB store wiring — runtime-validated end to end.** `RecallDbMemoryStore` (via the
  `RecallDb.Sdk` 0.2.1 NuGet client) maps a tenant → RecallDB tenant, a scope → collection (provisioned
  on demand with the scope's embedding dimension), a category → label, and a memory → document; supports
  vector/full-text/hybrid search. `MemoryService` computes embeddings through the scope's configured
  embedding endpoint and persists the RecallDB collection id back to the scope. Verified against real
  RecallDB + pgvector Postgres: a memory was embedded and stored in RecallDB, a **hybrid search ranked
  the relevant memory first and excluded an unrelated one**, **Chat with Memory** returned a grounded
  answer with citations, and the collections pass-through listed the provisioned collection. Store
  selection is configured via `StoreOptions` (RecallDB endpoint + admin key from settings).
- **PostgreSQL database provider:** the entity method implementations were made provider-agnostic
  (portable SQL over `DatabaseDriverBase`), and a `PostgresqlDatabaseDriver` (Npgsql) reuses them and
  the shared schema. The driver factory now returns Sqlite or Postgresql. **Runtime-validated** against
  a real `ankane/pgvector` container: schema creation, first-boot seeding, health, and scope CRUD all
  verified over the wire.
- **Docker deployment assets** (`docker/`): `compose.yaml` (validated with `docker compose config`)
  with named `jchristn77/notdory-*:v0.1.0` images + build stanzas, shared pgvector Postgres (init creates
  `notdory` + `recalldb` databases), RecallDB server/dashboard, two nginx instances (REST + MCP with SSE
  passthrough), and the pinned observability stack (Prometheus/Tempo/Loki/Alloy/Grafana); Dockerfiles
  for server/mcp/dashboard; Grafana provisioning + an Overview dashboard; a factory/demo overlay; and
  `DOCKERHUB_README.md`. New env overrides: `NOTDORY_REST_HOSTNAME`, `NOTDORY_DB_PORT`, `NOTDORY_RECALLDB_ENDPOINT`,
  `NOTDORY_RECALLDB_ADMIN_KEY`.
- **Request history:** capture (best-effort, in the PostRouting hook, health excluded) into a
  `request_history` table via `IRequestHistoryMethods`, and REST routes `GET /v1.0/api/requests`
  (admin sees all, tenant sees its own), `GET /v1.0/api/requests/{id}`, and `DELETE /v1.0/api/requests`.
  The dashboard's Request History view now has a live backend.
- **RecallDB collections pass-through:** `RecallDbCollectionProxy` + REST routes
  `/v1.0/api/tenants/{tid}/collections` (list/create/read/delete) that proxy to RecallDB's own API
  rather than re-implementing collection storage; returns a clear `RecallDbNotConfigured` when no
  RecallDB endpoint is set. Dashboard client methods added.
- Test suite (`Test.Shared` + `Test.Automated`, Touchstone): 15 automated tests, all passing (added
  request-history capture and the collections pass-through guard). The RecallDB/Postgres additions are
  contract-verified; Postgres is additionally runtime-validated against a real pgvector container.

- **All four database providers, live-tested.** The entity method implementations were made fully
  portable (a `PaginationClause` seam on the driver base; unique-key reads no longer use `LIMIT`), and
  **MySQL** (MySqlConnector, VARCHAR keys + inline indexes) and **SQL Server** (Microsoft.Data.SqlClient,
  guarded `CREATE TABLE`, inline indexes, `OFFSET/FETCH` pagination) providers were added with their own
  dialect DDL. The driver factory now serves Sqlite, Postgresql, Mysql, and SqlServer. Each server-based
  provider is validated by an **ephemeral-container round-trip test** (spins up the real database,
  creates the schema, exercises CRUD + JSON columns + pagination + tenant isolation, tears down) — all
  passing.
- **`notdory mcp install`** command in `NotDory.McpServer`: upserts a `notdory` MCP entry (`type: http`,
  `url: http://127.0.0.1:8720/mcp`, `x-access-key` header) into the agent client
  config (`~/.claude.json` or a project `.mcp.json` with `--project`), preserving all other servers and
  keys, writing a `.bak` backup. Flags: `--access-key`/`--host`/`--port`/`--url`/`--project`;
  reads defaults from `notdory.mcp.json` + env.
- **MCP connection docs** (`docs/`): `MCP_API.md` (the 10 tools), `CONNECTING_AGENTS.md` (Claude
  Code / Cursor / generic client setup + first-calls walkthrough), and a docs `README.md` index.

### Test suite

**266 automated tests, all passing** — a full-surface suite organized into layer suites: `ModelSuite`
(validation, clamps, defaults, PrettyId, JSON, settings — 38), `DatabaseSuite` (every entity's CRUD,
tenant scoping, pagination, JSON columns, SQL-injection safety — 59), `StoreSuite` (filesystem
single-file + hierarchy, factory, capabilities, unconfigured-store guards — 33), `ServiceSuite`
(health-check dedup/thresholds, embedding + inference OpenAI/Ollama parse + errors, memory + chat
services — 30), `AuthSuite` (authorization matrix + seeder — 6), `RestSuite` (every route, positive
and negative: 401/403/404/400/409/200/201/204 — 59), `McpSuite` (all 10 tools + negatives + raw
MCP handshake — 15), `InstallSuite` (config upsert, preservation, backup, header selection — 8), and
the original smoke + **three live ephemeral-container DB round-trips** (18). Positive and negative
paths across every layer and data path.

- **Bug found and fixed by the new tests:** `POST /v1.0/api/tenants` with no name returned 201 instead
  of 400 because the `Tenant` model defaulted `Name` to `"Default"`, masking the route's empty-name
  check. `Tenant.Name` now defaults to empty (consistent with every other entity), so the missing-name
  request is correctly rejected.

### Documentation

- Per-client MCP connection guides (`docs/INSTRUCTIONS_FOR_{CLAUDE_CODE,CODEX,GEMINI,CURSOR,MUX}.md`),
  modeled on Armada — each a paste-into-config guide with the connection snippet and the memory
  workflow.
