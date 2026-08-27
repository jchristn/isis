# Mnemosyne — Agent Memory Service — Scaffold Plan

> **Status:** Hypothesis / pre-implementation scaffold.
> **Product name:** `Mnemosyne` (placeholder; server prefix `mnemo`). Chosen to fit the existing single-evocative-name convention (Chronos, Armada, Verbex, Lattice, LiteGraph, Pneuma, Auralytic).
> **Conventions:** This plan is normative against `C:\code\agents\requirements` — `BACKEND_ARCHITECTURE.md` (Watson 7, provider-neutral DB, PrettyId, per-feature route registrars, thin `Program.cs`), `CODE_STYLE.md` (strict C#), `REPOSITORY_REQUIREMENTS.md` (repo layout), `AUTHENTICATION.md`, `TELEMETRY_REQUIREMENTS.md`, and `BACKEND_TEST_ARCHITECTURE.md` (shared test suite + multi-runner). Reference implementations to mirror: **LiteGraph** and **Pneuma** (both ship HTTP + MCP + request history + multi-runner tests).

---

## 1. Purpose

Give a model durable, structured, queryable memory that survives across sessions, harnesses, and projects — reachable two ways:

- **MCP surface** (for agent harnesses and models): a small, self-describing tool set to enumerate, create, read, update, delete **categories** and **memories**, plus **search** and a **guide** tool that teaches the agent how to use the store.
- **REST surface** (for humans and dashboards): full management — tenants, scopes, categories, memories, policies, storage config, request history, audit.

The store is **domain-agnostic**. It holds notes about a codebase (what lives where, what a function does, "I did X"), but equally about a book (`C:\Users\joelc\Documents\Books\Jiu_Jitsu_Fundamentals`), an inbox, a calendar, or **cross-cutting** guidance that applies everywhere (a house writing style, GitHub commit rules, review checklists).

The problem it removes: today an agent re-derives context every session — re-scanning a filesystem, re-reading files, re-learning conventions — burning tokens and wall-clock on work it already did once.

---

## 2. Core Concepts (Domain Model)

| Concept | What it is | Analogy |
|---|---|---|
| **Tenant** | Top-level isolation boundary (a person or org). Multi-tenant is first-class per house rules. | Account |
| **Scope** | A named memory space inside a tenant. A scope maps to a project, a book, "global", etc. Memories can be **scope-local** or **global** (visible to all scopes in the tenant). | Repo / workspace |
| **Category** | A named bucket of memories **with a description and usage instructions**. The instructions are the contract the model reads to know *when* to write here and *what shape* a good entry takes. User-seedable. | Folder + its README |
| **Memory** | One atomic fact/note: `title`, `body`, `tags[]`, `type`, structured `metadata`, `links[]`, provenance, timestamps. One memory = one idea (mirrors the existing Claude Code memory discipline). | A single note card |
| **Link** | A typed edge between memories (`[[slug]]` style). Enables a recall graph, not just a flat list. | Wiki backlink |
| **Policy / Instruction** | A special always-surfaced memory class for over-arching guidance (writing style, commit rules). Distinct from ordinary recall because it is injected proactively, not searched for. | Standing orders |
| **Profile (Seed Pack)** | A user-supplied bundle of suggested categories + instructions + policies that initializes a scope. "Set up this project the way I like it." | Project template |

### Memory record (canonical shape)

```jsonc
{
  "id": "mem_a1b2c3d4",              // PrettyId, entity prefix "mem_"
  "tenantId": "ten_...",
  "scopeId": "scp_...",              // null => global to tenant
  "categoryId": "cat_...",
  "slug": "filesystem-layout",       // stable, human/link addressable
  "title": "Where things live in the repo",
  "type": "project",                 // user|feedback|project|reference (extensible enum)
  "body": "src/ holds ... , tests in test/ ...",
  "summary": "One-line recall hook.", // cheap to return in list/search
  "tags": ["layout", "onboarding"],
  "links": ["build-commands", "commit-rules"],  // slugs
  "metadata": { "files": ["src/Foo.cs"], "confidence": 0.8 },
  "provenance": { "author": "agent|human", "sessionId": "...", "model": "claude-opus-4-8" },
  "salience": 0.72,                  // ranking signal, decays/boosts on use
  "createdUtc": "...", "updatedUtc": "...", "lastAccessedUtc": "...",
  "version": 3
}
```

### Category record

```jsonc
{
  "id": "cat_...", "tenantId": "...", "scopeId": "...|null",
  "name": "code-map",
  "description": "Notes on what each part of the codebase does.",
  "instructions": "Write one memory per subsystem. Title = subsystem name. Body = responsibilities + key files. Update rather than duplicate. Link to related subsystems.",
  "schemaHint": { "requiredTags": ["subsystem"] },   // optional soft contract
  "retention": { "maxItems": null, "ttlDays": null },
  "createdUtc": "...", "updatedUtc": "..."
}
```

---

## 3. Storage Providers (pluggable, provider-neutral)

The service defines **one storage abstraction** with interchangeable backends. This is the key flexibility the use case demands: the *same* API works whether artifacts live centrally or as flat files in the repo.

1. **Relational (structured)** — provider-neutral per house rules, implementations for `Sqlite`, `Mysql`, `Postgresql`, `SqlServer`. Categories, memories, links, policies map to real tables/columns/indexes (**not** a relational BLOB dump). Default for central deployments.
2. **BLOB/Object** — S3-compatible (and Azure Blob) for **large memory bodies** and attachments, with the relational store holding the index + metadata. Bodies over a threshold spill to object storage transparently.
3. **Flat-file / Filesystem** — memories as markdown files with YAML frontmatter in a repo subdirectory, plus a generated `MEMORY.md` index. This deliberately mirrors the memory format this environment already uses (one fact per file; frontmatter `name` / `description` / `metadata.type`; `[[link]]` bodies; index line per file). **Git-trackable**, diff-able, reviewable in a PR, and portable with the repo — ideal for the book and code use cases where the user wants the memory to travel with the artifact.

```
.mnemosyne/                      (or a user-chosen dir)
  memory/
    code-map/
      filesystem-layout.md
      build-commands.md
    changelog/
      2026-08-26-added-search.md
  policies/
    writing-style.md
    commit-rules.md
  MEMORY.md                      (index, one line per memory)
  mnemosyne.scope.json           (scope + category definitions/instructions)
```

Selection is per-scope: a scope declares its provider, so one tenant can keep global policies in Postgres while a specific project keeps its memory as flat files in that repo. The abstraction is exposed through domain-specific method interfaces (`ICategoryMethods`, `IMemoryMethods`, `ILinkMethods`, `IPolicyMethods`) — **not** a generic CRUD repository — per `BACKEND_ARCHITECTURE.md`.

### Optional recall index

An embeddings index (pgvector when on Postgres; a sidecar vector table on Sqlite; local flat vectors for the file provider) powers semantic `search_memory`. Embeddings are **optional and pluggable** — keyword/tag/FTS search is the always-on baseline so the service has zero hard dependency on an embedding provider.

---

## 4. MCP Surface (for agents)

Naming follows the existing `{service}_{entity}_{verb}` convention seen in Chronos/Armada. Every tool ships a rich description string so a cold model knows how to use it. Health/util tools (`mnemo_health`, `mnemo_server_info`, `ping`, `getTime`, `echo`) match the house MCP baseline.

**Discovery / self-teaching**
- `mnemo_guide` — returns the operating manual: available categories, their instructions, the active policies, and worked examples of good writes. **This is the first tool an agent should call.** It is what makes the store self-describing.
- `mnemo_policy_enumerate` — returns always-on guidance (writing style, commit rules) the model should honor without being asked.

**Categories**
- `mnemo_category_enumerate` (scope-aware, includes instructions)
- `mnemo_category_read`
- `mnemo_category_create` / `mnemo_category_update` / `mnemo_category_delete`

**Memories (within a category / scope)**
- `mnemo_memory_enumerate` — lists **summaries only** (token-cheap), filterable by category/tag/scope.
- `mnemo_memory_search` — keyword + semantic; returns ranked **snippets** with a `token_budget` cap.
- `mnemo_memory_read` — full body by id/slug (the only call that returns full text).
- `mnemo_memory_create` / `mnemo_memory_update` (upsert-by-slug) / `mnemo_memory_delete`
- `mnemo_memory_link` — assert a typed edge; `mnemo_memory_related` — walk the graph.

**Design rules that make the MCP surface efficient**
- List/search default to **summaries + snippets**, never full bodies. Full text is an explicit second hop. (Directly attacks token cost — see §8.)
- Every write returns the canonical slug so the agent can reference it later.
- `mnemo_memory_create` is **idempotent on `(scope, category, slug)`**: re-writing "filesystem-layout" updates in place rather than spawning duplicates.
- Responses carry a `token_budget` argument; the server truncates/ranks to fit rather than dumping.
- Optional `write_on_read` disabled by default; salience is bumped server-side on read so hot memories rank up over time.

---

## 5. REST Surface (for management)

Watson 7, per-feature route registrars, versioned under `/v1.0/`. Mirrors the MCP entities plus operational surface.

```
GET    /v1.0/health
GET    /v1.0/server/info

# tenants / scopes (admin)
GET    /v1.0/tenants/{tenantId}/scopes
POST   /v1.0/tenants/{tenantId}/scopes
PUT    /v1.0/tenants/{tenantId}/scopes/{scopeId}
DELETE /v1.0/tenants/{tenantId}/scopes/{scopeId}

# categories
GET    /v1.0/scopes/{scopeId}/categories
POST   /v1.0/scopes/{scopeId}/categories
GET|PUT|DELETE .../categories/{categoryId}

# memories
GET    /v1.0/scopes/{scopeId}/memories?category=&tag=&q=&limit=
POST   /v1.0/scopes/{scopeId}/memories
GET|PUT|DELETE .../memories/{memoryId}
POST   /v1.0/scopes/{scopeId}/memories/search        # body: query, budget, filters

# policies (cross-cutting instructions)
GET|POST /v1.0/tenants/{tenantId}/policies
GET|PUT|DELETE .../policies/{policyId}

# seed packs / profiles
POST   /v1.0/scopes/{scopeId}/seed                    # apply a Profile bundle

# storage config
GET|PUT /v1.0/scopes/{scopeId}/storage                # provider selection + creds ref

# operations
GET    /v1.0/scopes/{scopeId}/audit
GET    /v1.0/requests                                 # request history (house pattern)
POST   /v1.0/scopes/{scopeId}/compact                 # summarize/dedupe/prune
GET    /v1.0/scopes/{scopeId}/export                  # to flat-file bundle
POST   /v1.0/scopes/{scopeId}/import
```

Typed request/response DTOs (no `JsonElement` for fixed contracts). Explicit status codes, no tuple returns. Preflight + PostRouting + CORS per house rules.

---

## 6. Repository Layout (per `REPOSITORY_REQUIREMENTS.md` + `BACKEND_ARCHITECTURE.md`)

```
AgentMemory/
|-- src/
|   |-- Mnemosyne.sln
|   |-- Mnemosyne.Core/
|   |   |-- Constants.cs
|   |   |-- Database/
|   |   |   |-- DatabaseDriverBase.cs  DatabaseDriverFactory.cs  DatabaseSettings.cs
|   |   |   |-- DatabaseTypeEnum.cs     SchemaMigration.cs
|   |   |   |-- Interfaces/            (ICategoryMethods, IMemoryMethods, ILinkMethods, IPolicyMethods, IScopeMethods, IRequestHistoryMethods)
|   |   |   |-- Sqlite/  Mysql/  Postgresql/  SqlServer/   (each: Implementations/, Queries/, Sanitizer.cs, Converters.cs)
|   |   |-- Storage/                   (IStorageProvider + Relational/, ObjectStore/, FlatFile/ providers)
|   |   |-- Recall/                    (ISearchIndex + Keyword/, Embedding/ implementations)
|   |   |-- Enums/  Helpers/ (IdGenerator.cs)  Models/  Requests/  Responses/  Security/
|   |   |-- Services/ (Interfaces/, Implementations/: CategoryService, MemoryService, PolicyService, SeedService, CompactionService)
|   |-- Mnemosyne.Server/              (REST — Watson 7)
|   |   |-- Program.cs   MnemosyneServer.cs
|   |   |-- Settings/  Routes/ (CategoryRoutes, MemoryRoutes, PolicyRoutes, ScopeRoutes, SeedRoutes, StorageRoutes, AuditRoutes, HealthRoutes)
|   |   |-- Services/  Middleware/  Serialization/
|   |-- Mnemosyne.McpServer/           (MCP — mirrors LiteGraph/Pneuma McpServer)
|   |   |-- Program.cs   McpAuthenticatedRequestContext.cs
|   |   |-- Tools/ (GuideTool, CategoryTools, MemoryTools, PolicyTools, SearchTools)
|   |-- Test.Shared/  Test.Automated/  Test.Xunit/  Test.Nunit/
|-- sdk/                               (sdk/csharp, sdk/python, sdk/typescript — clients use 127.0.0.1)
|-- docker/                            (compose.yaml, factory/, factory/templates for HTTP+MCP)
|-- assets/
|-- docs/  (this plan)
|-- .gitignore  .dockerignore
|-- README.md  DOCKERHUB_README.md  CHANGELOG.md  LICENSE.md  (MIT)
|-- mnemosyne.json                     (settings)
```

Entity prefixes for PrettyId: `ten_`, `scp_`, `cat_`, `mem_`, `pol_`, `lnk_`, `seed_`.

---

## 7. Cross-Cutting Requirements

- **Auth** (`AUTHENTICATION.md`): API key / bearer, typed `RequestContext` established in the `AuthenticateRequest` hook and stashed in `ctx.Metadata`; tenant scoping enforced in the service layer, not just the route. MCP surface uses the authenticated MCP request-context pattern (`McpAuthenticatedRequestContext`).
- **Telemetry** (`TELEMETRY_REQUIREMENTS.md`): per-request timing, memory read/write/search counters, **tokens-served estimate per response**, salience/hit-rate metrics for effectiveness reporting.
- **Request history**: capture request/response for the audit and history routes (house pattern from Pneuma/LiteGraph).
- **Testing** (`BACKEND_TEST_ARCHITECTURE.md`): `Test.Shared` holds the suite; `Test.Automated` + xUnit + NUnit runners. Provider matrix (Sqlite/Mysql/Postgres/SqlServer + FlatFile) exercised via the shared suite. MCP tools tested against a live in-process server.
- **C# style** (`CODE_STYLE.md`): usings inside namespace, `_PascalCase` private fields, no tuples, no `var`, `ConfigureAwait(false)`, `CancellationToken` on every async, `IEnumerable` + async variant, XML docs on public surface, one class per file, custom exceptions.

---

## 8. Value Model (tokens, performance, throughput, efficiency, runtime)

Measured **abstractly** — real numbers depend on corpus size and harness — but the shape of the win is robust.

### 8.1 Token economics (the headline)

The dominant cost today is **re-acquisition of context**: an agent re-reads the filesystem/codebase each session because nothing persisted.

Let:
- `S` = tokens to scan/understand a workspace from cold (read files, build a mental map). For a medium repo this is easily **50k–300k+** tokens.
- `R` = tokens to recall the equivalent from Mnemosyne = a `guide` call + a few `search`/`read` hops returning summaries and targeted snippets. Realistically **1k–8k** tokens.
- `N` = number of sessions/tasks that reuse that context before it goes stale.
- `W` = one-time write cost to record memory during the first pass ≈ **2k–10k** tokens (mostly free — it rides on work already being done).

**Naïve cost (no memory):** `N × S`
**With memory:** `S + W + (N × R)`

**Savings ≈ `(N − 1) × S − W − N × R`.** For `S=150k`, `R=5k`, `W=8k`, over `N=10` sessions:
- Without: `10 × 150k = 1,500k` tokens on re-acquisition.
- With: `150k + 8k + 10 × 5k = 208k`.
- **~86% reduction** in context-acquisition tokens, and it improves with every additional session (the `S` term stops recurring).

Break-even is at **`N ≈ 2`**: memory pays for itself on the second reuse. Everything after is compounding.

### 8.2 Latency / runtime

- **Time-to-first-useful-action** drops from "scan the repo" (seconds-to-minutes of tool calls + model reading) to a single indexed lookup (**tens of ms** server-side; one round trip for the agent).
- Summaries-first responses mean the model spends fewer decoding steps before acting — **shorter agent turns**, less wall-clock per task.
- Flat-file provider adds ~0 infra latency (local disk); relational + FTS is single-digit ms at this scale; embedding search is bounded by the vector index, not the model.

### 8.3 Throughput

- Because each task consumes far less of the context window on re-acquisition, **more of the window is available for the actual work** → larger effective task size per session and fewer compaction/summarization cycles.
- Server-side: stateless REST/MCP handlers over a provider-neutral store scale horizontally; the store is read-heavy (recall ≫ write), which suits caching and read replicas. `ReaderWriterLockSlim`/read-optimized paths per house style.

### 8.4 Efficiency & quality (harder to quantify, real)

- **Consistency across sessions and harnesses**: the same policies (writing style, commit rules) apply whether the work happens in Claude Code, a cron agent, or a teammate's harness — no re-explaining.
- **Cross-project leverage**: over-arching memories (style, review checklist) are written once, recalled everywhere — the `N` in §8.1 spans *projects*, not just sessions, so the multiplier is larger than the single-repo model suggests.
- **Reduced drift/error**: the agent acts on a durable "where things live" record instead of re-guessing, lowering the rate of wrong-file edits and their rework cost (rework is pure wasted tokens + latency).
- **Auditability**: memory is inspectable (flat files diff in a PR; REST audit for central), so a human can correct a bad memory once instead of the model repeating a mistake N times.

### 8.5 Costs / where it can lose

- Cold benefit is negative for **one-shot** tasks (`N=1`): you pay `W` and never amortize. Mitigation: writes are cheap and mostly ride existing work; `guide` discourages gratuitous writes.
- **Staleness risk**: a wrong memory is worse than none. Mitigations: provenance + timestamps, `lastAccessed`/salience decay, a `compact` operation, and confidence metadata so recall can down-weight old notes.
- **Write discipline**: without category instructions the store devolves into a junk drawer. The `guide`/`instructions` contract and idempotent upsert-by-slug are the countermeasures.

---

## 9. Build Phases

1. **Core + FlatFile provider + MCP server** — the fastest path to real value; agent can read/write markdown memory in a repo. (MVP; validates the token thesis end-to-end.)
2. **Relational provider (Sqlite first) + REST management surface + auth + request history.**
3. **Search/recall** — keyword/FTS baseline, then optional embeddings; `token_budget` ranking.
4. **Policies + Seed Packs + compaction** — cross-cutting guidance and store hygiene.
5. **Remaining DB providers (Mysql/Postgres/SqlServer) + Object store spill + SDKs + Docker factory + dashboard.**
6. **Multi-runner test matrix, telemetry dashboards, DOCKERHUB packaging.**

---

## 10. Open Questions for the User

- Single-tenant-per-deployment (simpler, self-hosted-per-person) vs. true multi-tenant SaaS from day one?
- Default provider for the common case — **flat-file-in-repo** (travels with the artifact, PR-reviewable) or **central Sqlite/Postgres** (queryable across projects)? (Recommendation: flat-file for MVP, central for cross-project policies.)
- Is semantic search required for v1, or is keyword/tag/FTS sufficient until the corpus grows?
- Should policies auto-inject into the agent's context via `guide`, or only surface on explicit request?
