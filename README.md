<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img src="assets/notdory.png" width="192" height="192" alt="NotDory" />
</p>

<h1 align="center">NotDory — Agent Memory Platform</h1>

<p align="center"><em>Your agents forget everything. NotDory doesn't.</em></p>

---

Dory, the lovable blue tang from *Finding Nemo*, forgets everything a few seconds after it happens. She's
charming, she means well, and she will introduce herself to you again tomorrow. Most AI agents are Dory: every
new session greets your codebase like it's never seen it before, re-reading the same files, re-learning the same
conventions, re-asking the same questions, and burning tokens just to get back to where they were yesterday.

**NotDory is not Dory.** It gives your agents a memory that sticks, so they:

- **Remember.** Decisions, conventions, facts, and "we tried that, it didn't work" survive across sessions,
  harnesses, and projects.
- **Pick up where they left off.** Session start hands the agent its project's context before the first turn.
- **Use fewer tokens.** Recalling a note is far cheaper than re-scanning a repository to rediscover it.
- **Get to outcomes faster.** Less rediscovery, more doing.
- **Stop repeating mistakes.** A correction or preference is saved once and honored from then on.
- **Share what they learn.** What Claude Code learns, Codex, Cursor, Gemini, and Mux can recall.

Just keep swimming, and keep remembering.

> ⚠️ **v0.1.0 — ALPHA. Everything is subject to change.** APIs, data models, storage layouts,
> configuration keys, MCP tool names, database schemas, and dashboard surfaces **will change** —
> potentially in breaking ways and **without migration paths** between 0.1.x builds. Do not use it
> for production memory you cannot afford to lose, and expect to re-create data across upgrades. Pin
> to an exact image tag (e.g. `v0.1.0`) and read `CHANGELOG.md` before updating.

NotDory gives AI agents **durable memory** that survives across sessions, harnesses, and projects. The
agent manages that memory itself — writing, organizing, and recalling it over **MCP** — while
operators get a **REST** API and a React **dashboard** for management. The dominant cost in agentic
work is re-acquiring context (re-scanning a filesystem, re-reading files, re-learning conventions
every session); NotDory turns that recurring cost into a one-time write plus cheap recall.

Memory is organized into **scopes** (a project, a book, or "global"), **categories** (buckets with
usage instructions the model reads before writing), and **memories** (atomic notes with tags, links,
provenance, and salience). Cross-cutting **policies** (a house writing style, GitHub commit rules)
apply everywhere and are surfaced to the agent proactively. NotDory is domain-agnostic: it works equally
for code ("what lives where", "what this function does", "I did X"), writing, email, or calendar work.

## What NotDory is

- **A memory layer for agents.** Agents read and write structured memory over MCP; the model decides
  what to remember and recalls it on demand.
- **Structured, not a blob.** Scopes, categories with instructions, tags, links, and policies give the
  model the *right* context proactively — not just nearest-neighbor chunks.
- **Pluggable storage per scope.** Choose semantic search (RecallDB), git-trackable flat files
  (Filesystem), or both at once (RecallDB with a filesystem mirror) on a scope-by-scope basis.
- **Multi-tenant and observable.** Tenants, users, and credentials with RBAC; metrics, traces, and
  logs wired into Grafana/Prometheus/Tempo/Loki out of the box.

## What NotDory is not

- **Not production-ready.** It is alpha; see the warning above.
- **Not a general-purpose vector database.** It is memory-shaped (scopes/categories/memories with
  instructions), not a bag of embeddings. RecallDB is the vector store *behind* it.
- **Not a document-RAG pipeline.** It does not chunk and index arbitrary document corpora; it stores
  the durable facts and decisions an agent chooses to keep.
- **Not an agent framework or a chat product.** It is the memory an agent plugs into, not the agent.
- **Not a hosted service.** You run it yourself (Docker).

## Benefits

- **Stop re-acquiring context.** Break-even is roughly the second reuse; after that the savings
  compound (see `archive/NOTDORY_PLAN.md` §Value Model).
- **Agent-managed.** The agent curates its own memory over MCP — no manual data entry.
- **Right context, proactively.** Category instructions and cross-cutting policies mean the model is
  told *how* to use a memory space, not just handed rows.
- **Fits your workflow.** Keep memory semantic (RecallDB), as PR-reviewable markdown that travels
  inside the repo (Filesystem), or both: semantic search with a markdown copy in the repo.

## Use cases

- **Coding agents** — remember repo layout, conventions, "what this function does", and decisions, so
  a fresh session doesn't re-scan the tree.
- **Writing** — a house style, character/world facts, and continuity across a long document.
- **Email / calendar assistants** — preferences, recurring participants, and standing instructions.
- **Team knowledge** — a shared tenant so multiple agents (and people) draw on the same memory.

## Quick start (Docker)

Docker is the supported way to run NotDory. You need Docker with Compose.

```bash
git clone https://github.com/jchristn/notdory
cd notdory/docker
cp .env.example .env      # then edit the secrets before anything non-local
docker compose up -d --build
```

Then:

- **Dashboard** — <http://127.0.0.1:8701>. Sign in with the seeded admin **`admin@notdory.local`** /
  **`notdoryadmin`**, tenant **Default**. Change these before any shared deployment.
- **REST API** — <http://127.0.0.1:8700> (direct) or <http://127.0.0.1:8080> (via nginx). OpenAPI at
  `/openapi.json`; see `docs/REST_API.md` and the Postman collection at `docs/notdory.postman_collection.json`.
- **MCP** — `http://127.0.0.1:8720/mcp`. Connect an agent with its credential access key (a bearer
  token, or the `x-access-key` header); no secret is sent. See `docs/CONNECTING_AGENTS.md`.
- **Observability** — Grafana <http://127.0.0.1:3000>, Prometheus <http://127.0.0.1:9090>, RecallDB
  console <http://127.0.0.1:8601>.

**Reranking (recommended).** A cross-encoder reranker is the largest retrieval improvement measured (see
`benchmarks/RESULTS.md`). Start the stack with `--profile rerank` (CPU) or `--profile rerank-gpu` (NVIDIA GPU; pick the
Text Embeddings Inference image tag for your GPU generation). NotDory waits for the reranker to answer, adds it as an
inference endpoint (a cross-encoder), and attaches it to new semantic scopes. On CPU a reranked search takes roughly 0.3 to 0.5 s; on a GPU, tens
of milliseconds. If the reranker is unreachable, searches fall back to retrieval order.

```bash
docker compose --profile rerank up -d        # or: --profile rerank-gpu
```

Helper scripts (Windows): `docker/update.bat` pulls the latest images and recreates the stack;
`docker/factory/reset.bat` wipes the volumes and brings up a seeded demo environment.

## Connecting your agent harness

Once the stack is up, point any MCP-capable agent at the MCP endpoint:

```text
http://127.0.0.1:8720/mcp
```

Authenticate with your tenant credential **access key** — sent as the `x-access-key` header (or an
`Authorization: Bearer <accessKey>` token). The access key is the public, transferable capability
token; the **secret key is never sent** and never leaves your machine. The local-dev default key is
`notdorydefaultkey`; create a real one in the dashboard under **Credentials** and use a least-privilege key.

The quickest way is the ready-made installers in `scripts/` — one per harness, for `windows`, `macos`,
and `linux`. Each takes the access key as its first argument (or reads `NOTDORY_ACCESS_KEY`), writes a
`notdory` entry into that client's config, backs up the existing file, and leaves everything else intact.
Run the one for your OS; the examples below use `linux` (swap in `macos/…` or `windows\…\*.bat`).

> The `<accessKey>` below is your credential access key. It is optional — omit it to use the
> local-dev default **`notdorydefaultkey`**.

| Harness | Install (Linux/macOS) | Windows | Client config it writes |
|---|---|---|---|
| **Claude Code** | `sh scripts/linux/install-claude.sh <accessKey>` | `scripts\windows\install-claude.bat <accessKey>` | `claude mcp add --scope user` (`~/.claude.json`) plus a SessionStart hook (`~/.claude/settings.json`) that loads each project's memory before the first turn |
| **Codex** | `sh scripts/linux/install-codex.sh <accessKey>` | `scripts\windows\install-codex.bat <accessKey>` | `~/.codex/config.json` |
| **Cursor** | `sh scripts/linux/install-cursor.sh <accessKey>` | `scripts\windows\install-cursor.bat <accessKey>` | `~/.cursor/mcp.json` |
| **Gemini CLI** | `sh scripts/linux/install-gemini.sh <accessKey>` | `scripts\windows\install-gemini.bat <accessKey>` | `~/.gemini/settings.json` |
| **Mux** | `sh scripts/linux/install-mux.sh <accessKey>` | `scripts\windows\install-mux.bat <accessKey>` | `~/.mux/mcp-servers.json` |

Then **restart the client** to load the server. Prefer to wire it yourself? For Claude Code (replace
`<accessKey>`, or use the local-dev default `notdorydefaultkey`):

```bash
claude mcp add --transport http notdory http://127.0.0.1:8720/mcp --header "x-access-key: <accessKey>"
```

Each installer honors `NOTDORY_MCP_URL` (endpoint) and `NOTDORY_ACCESS_KEY`, plus a per-harness config
override (`NOTDORY_CODEX_CONFIG`, `NOTDORY_CURSOR_CONFIG`, `NOTDORY_GEMINI_CONFIG`, `NOTDORY_MUX_CONFIG`). Matching
`remove-*` scripts undo the change.

**First call:** tools appear namespaced under the server key, e.g. `notdory.session_start` (Mux) or
`mcp__notdory__session_start` (Claude Code). NotDory sends its operating procedure in the MCP `initialize` result, which
harnesses place in the model's system prompt: call **`session_start`** with the project name, search memory before
answering or changing code, and save decisions and facts as you go. `session_start` returns the project's scope
(created if new), its categories and instructions, and the most recent memories; no tool needs a `tenantId`. The
`notdory mcp install` installer also adds a Claude Code SessionStart hook that injects that context before the first
turn. What agents are told on connect (the instructions and every tool description) is editable in the dashboard
(**Agent onboarding**) or through `PUT /v1.0/api/agent-protocol`. Full config and the tool contract are
in [`docs/CONNECTING_AGENTS.md`](docs/CONNECTING_AGENTS.md) and [`docs/MCP_API.md`](docs/MCP_API.md).

## Architecture

```
Agent harness ──MCP──▶ nginx ─▶ NotDory.McpServer (Voltaic 2.2.1) ─proxy─┐
Operator/UI  ──REST──▶ nginx ─▶ NotDory.Server (Watson 7.2) ◀────────────┘
                                     │                 │
                     NotDory metadata│                 │memory content + vectors
                       (Postgres     │                 │(RecallDb.Sdk over HTTP)
                        db: notdory)  ▼                 ▼
                                ┌───────────┐    ┌──────────────┐
                                │ Postgres  │    │  RecallDB    │
                                │ (shared)  │    │  db: recalldb│
                                └───────────┘    └──────────────┘
   Embedding endpoint ◀─ NotDory computes vectors
   (rerankers are inference endpoints: a cross-encoder or a chat model reorders search candidates)
   Inference endpoint ◀─ chat answers; optional query rewriting, splitting, expansion   (all health-checked)
```

- **RecallDB** is the default system of record for memory content, embeddings, and retrieval, on a
  shared Postgres instance.
- **NotDory** owns a separate `notdory` database on that same Postgres instance for what RecallDB has no
  schema for: category instructions, policies, seed packs, the memory link graph,
  slugs/titles/summaries, model-endpoint configs, tenancy/auth, and request history.
- **NotDory.McpServer** is a standalone process that authenticates the caller and proxies the NotDory REST API.

## How it works

1. **Agents talk MCP, operators talk REST.** Agents call the MCP tools (`session_start`,
   `memory_upsert`, `memory_search`, `chat`, and scope, category, endpoint, and instruction management;
   no `notdory_` prefix, your client namespaces them); operators and the dashboard use the tenant-scoped
   REST API.
2. **Authentication.** Interactive users sign in with **email + password** and receive a session
   token (`Authorization: Bearer`); automation and MCP authenticate with a credential **access key**
   (sent as a bearer token or `x-access-key`), which authenticates on its own as a capability token —
   an `x-secret-key` is optional and validated only when present. Single-header MCP clients such as Mux
   send just the access key. Admin authority comes from user `IsAdmin` / `IsTenantAdmin` flags. See
   `docs/REST_API.md`.
3. **Storage is chosen per scope.** A scope binds to RecallDB (semantic/hybrid) or Filesystem (flat
   files), and a RecallDB scope can also mirror every memory to the filesystem. RecallDB scopes require an embedding endpoint; NotDory computes the vector and
   passes it to RecallDB. A scope's embedding model and dimension are fixed at creation — changing them
   means a new scope and re-embedding.
4. **The model gets guidance, not just rows.** Category instructions and cross-cutting policies are
   surfaced to the agent so it knows how to use a space, and the `guide` tool returns an onboarding
   manifest for a scope.

### Backing stores — what each provides

NotDory stores memory through a pluggable `IMemoryStore`, chosen **per scope**. Capabilities differ by
provider — pick the one that matches what you need:

| Capability | **RecallDB** (default) | **Filesystem** | **RecallDB + filesystem mirror** |
|---|:--:|:--:|:--:|
| System of record for memory content | RecallDB (Postgres) | Flat files at a target path | RecallDB (Postgres) |
| Keyword / full-text search | ✅ (`ts_rank`) | ⚠️ term matching over the files | ✅ (`ts_rank`) |
| **Semantic (vector) search** | ✅ | ❌ | ✅ |
| **Hybrid search** (vector + lexical, weighted) | ✅ | ❌ | ✅ |
| Requires an embedding model endpoint | ✅ (NotDory computes vectors) | ❌ | ✅ |
| Label / tag / date filtering | ✅ | ⚠️ metadata-limited | ✅ |
| Positional neighbor retrieval | ✅ (`IncludeNeighbors`) | ❌ | ✅ |
| Runs without Docker | ❌ (needs Postgres + RecallDB) | ✅ | ❌ |
| Git-trackable / PR-reviewable memory | ❌ | ✅ (single file, hierarchy, or OKF bundle) | ✅ (OKF bundle) |
| Travels inside the target repository | ❌ | ✅ | ✅ (a copy; search uses RecallDB) |

**Only RecallDB provides semantic and hybrid search.** **Filesystem** is keyword/metadata only, and is the choice
when you want memory to live *inside* a repository (single file, an organized markdown hierarchy, or an Open Knowledge
Format bundle) and be reviewed in a pull request.

**To get both**, create a RecallDB scope with a `targetPath` (the mirror is on by default when one is given; send
`filesystemMirror: false` to opt out). Agents get this automatically: `session_start` with `path` set to the repository
root mirrors a new scope there when the NotDory server can see that directory. Every memory is written to
RecallDB and, concurrently, to an Open Knowledge Format bundle in a `.okf` directory under `targetPath` (one markdown
file per memory with YAML frontmatter, plus a generated `index.md`). Point `targetPath` at the repository root; NotDory
appends `.okf` itself, so the bundle stays out of the way of the repository's own files. RecallDB stays the system of record and serves every search; the bundle is
a git-trackable copy. A write fails if either side fails, so the copy never silently falls behind. Turning the mirror
on for an existing scope, or changing its `targetPath`, copies the existing memories into the bundle. Deleting the
scope leaves the files in place. `targetPath` is a directory on the NotDory server host: in Docker, bind-mount the
repository into the `notdory-server` container and use the path inside the container.

## Retrieval

A search runs a vector search and a full-text search in parallel, fuses them by weighted reciprocal rank with a small
recency signal, rolls chunks up to one hit per memory, optionally reranks the candidates with a cross-encoder, and then
applies supersession (a replaced fact ranks after its replacement) and optional link expansion. Chat grounds its
answers on the best whole chunk of each retrieved memory, cites every claim, understands follow-up questions sent with
the conversation's history, and says so when memory does not hold the answer. Optional extra queries (caller-supplied,
split from a multi-part question, or a model-drafted answer and keywords) are fused by weight below the original
query. Each scope chooses the model for every job (embedding, reranking, chat answers, and query rewriting and
expansion) and which optional steps run, from REST, MCP, or the dashboard.

On the benchmark suite, hybrid retrieval reaches nDCG@10 of 0.84 to 0.91 on the memory-style datasets, or 0.88 to 0.94
with the cross-encoder, and beats the published BM25 and dense-model baselines on BEIR SciFact. See
[SEARCH_PIPELINE.md](SEARCH_PIPELINE.md) for every stage with its rationale, implementation, and measured effect, and
[RETRIEVAL_IMPROVEMENTS.md](archive/RETRIEVAL_IMPROVEMENTS.md) for what has been tried and what is next.

## Projects

| Project | Purpose |
|---|---|
| `src/NotDory.Core` | Models, enums, PrettyId, database providers (Sqlite/Mysql/Postgresql/SqlServer), memory stores, services |
| `src/NotDory.Server` | REST API (Watson 7.2) + dashboard host + OpenAPI |
| `src/NotDory.McpServer` | MCP server (Voltaic 2.2.1), agent-facing tools |
| `dashboard` | React 19 / Vite 6 management dashboard |
| `docker` | Compose stack, per-service Dockerfiles, factory/demo seed |
| `docs` | REST API reference, MCP API, agent-connection guides, product plan |

## Benchmarks

`benchmarks/` holds a reproducible benchmark suite covering retrieval accuracy (including BEIR SciFact and
LongMemEval), chat-with-memory accuracy, agent-in-the-loop task success over MCP, and load. It runs against an
isolated stack so it never touches a deployment. See [benchmarks/README.md](benchmarks/README.md) for how to run it
and [benchmarks/RESULTS.md](benchmarks/RESULTS.md) for the current baseline. With NotDory connected, Claude Code
(haiku) completed 96% of memory-dependent tasks, against 17 to 21% without memory.

## Issues & discussion

- **Bugs / feature requests:** open an issue at <https://github.com/jchristn/notdory/issues>.
- **Questions / ideas:** start a thread at <https://github.com/jchristn/notdory/discussions>.
- Because this is alpha, please include the image tag / build you are on, your backing store, and
  clear reproduction steps.

## Status

Under active initial construction. See `CHANGELOG.md`.

## License

MIT — see `LICENSE.md`.
