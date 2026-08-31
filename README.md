<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img src="assets/logo.png" width="192" height="192" alt="Isis" />
</p>

<h1 align="center">Isis — Agent Memory Platform</h1>

<p align="center"><em>Durable memory your agents manage themselves — so every session starts where the last left off.</em></p>

---

> ⚠️ **v0.1.0 — ALPHA. Everything is subject to change.** APIs, data models, storage layouts,
> configuration keys, MCP tool names, database schemas, and dashboard surfaces **will change** —
> potentially in breaking ways and **without migration paths** between 0.1.x builds. Do not use it
> for production memory you cannot afford to lose, and expect to re-create data across upgrades. Pin
> to an exact image tag (e.g. `v0.1.0`) and read `CHANGELOG.md` before updating.

Isis gives AI agents **durable memory** that survives across sessions, harnesses, and projects. The
agent manages that memory itself — writing, organizing, and recalling it over **MCP** — while
operators get a **REST** API and a React **dashboard** for management. The dominant cost in agentic
work is re-acquiring context (re-scanning a filesystem, re-reading files, re-learning conventions
every session); Isis turns that recurring cost into a one-time write plus cheap recall.

Memory is organized into **scopes** (a project, a book, or "global"), **categories** (buckets with
usage instructions the model reads before writing), and **memories** (atomic notes with tags, links,
provenance, and salience). Cross-cutting **policies** (a house writing style, GitHub commit rules)
apply everywhere and are surfaced to the agent proactively. Isis is domain-agnostic: it works equally
for code ("what lives where", "what this function does", "I did X"), writing, email, or calendar work.

## What Isis is

- **A memory layer for agents.** Agents read and write structured memory over MCP; the model decides
  what to remember and recalls it on demand.
- **Structured, not a blob.** Scopes, categories with instructions, tags, links, and policies give the
  model the *right* context proactively — not just nearest-neighbor chunks.
- **Pluggable storage per scope.** Choose semantic search (RecallDB), lexical search (Verbex), or
  git-trackable flat files (Filesystem) on a scope-by-scope basis.
- **Multi-tenant and observable.** Tenants, users, and credentials with RBAC; metrics, traces, and
  logs wired into Grafana/Prometheus/Tempo/Loki out of the box.

## What Isis is not

- **Not production-ready.** It is alpha; see the warning above.
- **Not a general-purpose vector database.** It is memory-shaped (scopes/categories/memories with
  instructions), not a bag of embeddings. RecallDB is the vector store *behind* it.
- **Not a document-RAG pipeline.** It does not chunk and index arbitrary document corpora; it stores
  the durable facts and decisions an agent chooses to keep.
- **Not an agent framework or a chat product.** It is the memory an agent plugs into, not the agent.
- **Not a hosted service.** You run it yourself (Docker).

## Benefits

- **Stop re-acquiring context.** Break-even is roughly the second reuse; after that the savings
  compound (see `docs/ISIS_PLAN.md` §Value Model).
- **Agent-managed.** The agent curates its own memory over MCP — no manual data entry.
- **Right context, proactively.** Category instructions and cross-cutting policies mean the model is
  told *how* to use a memory space, not just handed rows.
- **Fits your workflow.** Keep memory semantic (RecallDB), lexical (Verbex), or as
  PR-reviewable markdown that travels inside the repo (Filesystem).

## Use cases

- **Coding agents** — remember repo layout, conventions, "what this function does", and decisions, so
  a fresh session doesn't re-scan the tree.
- **Writing** — a house style, character/world facts, and continuity across a long document.
- **Email / calendar assistants** — preferences, recurring participants, and standing instructions.
- **Team knowledge** — a shared tenant so multiple agents (and people) draw on the same memory.

## Quick start (Docker)

Docker is the supported way to run Isis. You need Docker with Compose.

```bash
git clone https://github.com/jchristn/isis
cd isis/docker
cp .env.example .env      # then edit the secrets before anything non-local
docker compose up -d --build
```

Then:

- **Dashboard** — <http://127.0.0.1:8701>. Sign in with the seeded admin **`admin@isis.local`** /
  **`isisadmin`**, tenant **Default**. Change these before any shared deployment.
- **REST API** — <http://127.0.0.1:8700> (direct) or <http://127.0.0.1:8080> (via nginx). OpenAPI at
  `/openapi.json`; see `docs/REST_API.md` and the Postman collection at `docs/isis.postman_collection.json`.
- **MCP** — `http://127.0.0.1:8720/mcp`. Connect an agent with its credential access key (a bearer
  token, or the `x-access-key` header); no secret is sent. See `docs/CONNECTING_AGENTS.md`.
- **Observability** — Grafana <http://127.0.0.1:3000>, Prometheus <http://127.0.0.1:9090>, RecallDB
  console <http://127.0.0.1:8601>.

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
`isisdefaultkey`; create a real one in the dashboard under **Credentials** and use a least-privilege key.

The quickest way is the ready-made installers in `scripts/` — one per harness, for `windows`, `macos`,
and `linux`. Each takes the access key as its first argument, which is **optional: omit it to use the
local-dev default `isisdefaultkey`** (or set `ISIS_ACCESS_KEY`). The installer writes an `isis` entry
into that client's config, backs up the existing file, and leaves everything else intact. Run the one
for your OS; the examples below use `linux` (swap in `macos/…` or `windows\…\*.bat`). Replace
`isisdefaultkey` with your own credential access key for anything beyond local development.

| Harness | Install (Linux/macOS) | Windows | Client config it writes |
|---|---|---|---|
| **Claude Code** | `sh scripts/linux/install-claude.sh isisdefaultkey` | `scripts\windows\install-claude.bat isisdefaultkey` | via `claude mcp add` (`~/.claude.json`) |
| **Codex** | `sh scripts/linux/install-codex.sh isisdefaultkey` | `scripts\windows\install-codex.bat isisdefaultkey` | `~/.codex/config.json` |
| **Cursor** | `sh scripts/linux/install-cursor.sh isisdefaultkey` | `scripts\windows\install-cursor.bat isisdefaultkey` | `~/.cursor/mcp.json` |
| **Gemini CLI** | `sh scripts/linux/install-gemini.sh isisdefaultkey` | `scripts\windows\install-gemini.bat isisdefaultkey` | `~/.gemini/settings.json` |
| **Mux** | `sh scripts/linux/install-mux.sh isisdefaultkey` | `scripts\windows\install-mux.bat isisdefaultkey` | `~/.mux/mcp-servers.json` |

Then **restart the client** to load the server. Prefer to wire it yourself? For Claude Code (the
`isisdefaultkey` below is the local-dev default — swap in your own access key otherwise):

```bash
claude mcp add --transport http isis http://127.0.0.1:8720/mcp --header "x-access-key: isisdefaultkey"
```

Each installer honors `ISIS_MCP_URL` (endpoint) and `ISIS_ACCESS_KEY`, plus a per-harness config
override (`ISIS_CODEX_CONFIG`, `ISIS_CURSOR_CONFIG`, `ISIS_GEMINI_CONFIG`, `ISIS_MUX_CONFIG`). Matching
`remove-*` scripts undo the change.

**First calls:** tools appear namespaced under the server key — e.g. `isis.whoami` (Mux) or
`mcp__isis__whoami` (Claude Code). Call **`whoami`** first to get your `tenantId` (every other tool
needs it), then **`instructions`** for the tenant's usage guide. Full config and the tool contract are
in [`docs/CONNECTING_AGENTS.md`](docs/CONNECTING_AGENTS.md) and [`docs/MCP_API.md`](docs/MCP_API.md).

## Architecture

```
Agent harness ──MCP──▶ nginx ─▶ Isis.McpServer (Voltaic 0.6.1) ─proxy─┐
Operator/UI  ──REST──▶ nginx ─▶ Isis.Server (Watson 7.1) ◀────────────┘
                                     │                 │
                        Isis metadata│                 │memory content + vectors
                          (Postgres  │                 │(RecallDb.Sdk over HTTP)
                           db: isis)  ▼                 ▼
                                ┌───────────┐    ┌──────────────┐
                                │ Postgres  │    │  RecallDB    │
                                │ (shared)  │    │  db: recalldb│
                                └───────────┘    └──────────────┘
   Embedding endpoint ◀─ Isis computes vectors
   Inference endpoint ◀─ Isis summarizes / compacts   (health-checked, dedup by method+URL+auth)
```

- **RecallDB** is the default system of record for memory content, embeddings, and retrieval, on a
  shared Postgres instance.
- **Isis** owns a separate `isis` database on that same Postgres instance for what RecallDB has no
  schema for: category instructions, policies, seed packs, the memory link graph,
  slugs/titles/summaries, model-endpoint configs, tenancy/auth, and request history.
- **Isis.McpServer** is a standalone process that authenticates the caller and proxies the Isis REST API.

## How it works

1. **Agents talk MCP, operators talk REST.** Agents call `isis_*` tools (scope/category/memory
   create, upsert, search, read, enumerate, guide); operators and the dashboard use the tenant-scoped
   REST API.
2. **Authentication.** Interactive users sign in with **email + password** and receive a session
   token (`Authorization: Bearer`); automation and MCP authenticate with a credential **access key**
   (sent as a bearer token or `x-access-key`), which authenticates on its own as a capability token —
   an `x-secret-key` is optional and validated only when present. Single-header MCP clients such as Mux
   send just the access key. Admin authority comes from user `IsAdmin` / `IsTenantAdmin` flags. See
   `docs/REST_API.md`.
3. **Storage is chosen per scope.** A scope binds to RecallDB (semantic/hybrid), Verbex (lexical), or
   Filesystem (flat files). RecallDB scopes require an embedding endpoint; Isis computes the vector and
   passes it to RecallDB. A scope's embedding model and dimension are fixed at creation — changing them
   means a new scope and re-embedding.
4. **The model gets guidance, not just rows.** Category instructions and cross-cutting policies are
   surfaced to the agent so it knows how to use a space, and the `guide` tool returns an onboarding
   manifest for a scope.

### Backing stores — what each provides

Isis stores memory through a pluggable `IMemoryStore`, chosen **per scope**. Capabilities differ by
provider — pick the one that matches what you need:

| Capability | **RecallDB** (default) | **Verbex** | **Filesystem** |
|---|:--:|:--:|:--:|
| System of record for memory content | RecallDB (Postgres) | Isis database | Flat files at a target path |
| Keyword / full-text search | ✅ (`ts_rank`) | ✅ (TF-IDF inverted index) | ⚠️ DB-native `LIKE` (or Verbex sidecar) |
| **Semantic (vector) search** | ✅ | ❌ | ❌ |
| **Hybrid search** (vector + lexical, weighted) | ✅ | ❌ | ❌ |
| Requires an embedding model endpoint | ✅ (Isis computes vectors) | ❌ | ❌ |
| Label / tag / date filtering | ✅ | ✅ (labels/tags) | ⚠️ metadata-limited |
| Positional neighbor retrieval | ✅ (`IncludeNeighbors`) | ❌ | ❌ |
| Runs without Docker | ❌ (needs Postgres + RecallDB) | ⚠️ in-proc mode, or server | ✅ |
| Git-trackable / PR-reviewable memory | ❌ | ❌ | ✅ (single file or hierarchy) |
| Travels inside the target repository | ❌ | ❌ | ✅ |

**Only RecallDB provides semantic and hybrid search.** Verbex and Filesystem are keyword/metadata
only. **Filesystem** is the choice when you want memory to live *inside* a repository (single file or
an organized markdown hierarchy) and be reviewed in a pull request.

## Projects

| Project | Purpose |
|---|---|
| `src/Isis.Core` | Models, enums, PrettyId, database providers (Sqlite/Mysql/Postgresql/SqlServer), memory stores, services |
| `src/Isis.Server` | REST API (Watson 7.1) + dashboard host + OpenAPI |
| `src/Isis.McpServer` | MCP server (Voltaic 0.6.1), agent-facing tools |
| `dashboard` | React 19 / Vite 6 management dashboard |
| `docker` | Compose stack, per-service Dockerfiles, factory/demo seed |
| `docs` | REST API reference, MCP API, agent-connection guides, product plan |

## Issues & discussion

- **Bugs / feature requests:** open an issue at <https://github.com/jchristn/isis/issues>.
- **Questions / ideas:** start a thread at <https://github.com/jchristn/isis/discussions>.
- Because this is alpha, please include the image tag / build you are on, your backing store, and
  clear reproduction steps.

## Status

Under active initial construction. See `CHANGELOG.md`.

## License

MIT — see `LICENSE.md`.
