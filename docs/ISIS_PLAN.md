# Isis — Agent Memory Platform — Product Plan

> **Status:** Actionable product plan. Supersedes `docs/SCAFFOLD_PLAN.md` (early hypothesis under the placeholder name "Mnemosyne").
> **Product name:** `Isis`. Server prefix `isis`, MCP tool prefix ``, PrettyId prefixes below.
> **Governing requirements:** everything here complies with `C:\code\agents\requirements` — `BACKEND_ARCHITECTURE.md`, `AUTHENTICATION.md`, `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `TELEMETRY_REQUIREMENTS.md`, `BACKEND_TEST_ARCHITECTURE.md`, `REPOSITORY_REQUIREMENTS.md`, `CODE_STYLE.md`, `I18N.md`. Where a requirement doc conflicts with a reference implementation, the requirement doc wins.
> **Primary reference implementations (all local):** RecallDB (`C:\Code\RecallDB`), Verbex (`C:\Code\Verbex`), Voltaic (`C:\Code\Voltaic`), Conductor (`C:\Code\Conductor`), Partio (`C:\Code\Partio`), Pneuma-Old (`C:\Code\Pneuma-Old`), AssistantHub (`C:\Code\AssistantHub`), LiteGraph, Lattice, Armada, Tempo.
> **Target framework:** **.NET 10** (`net10.0`). RecallDB SDK (`RecallDb.Sdk` 0.2.1) and Voltaic 0.6.1 both require it; libraries multi-target `net8.0;net10.0` where practical, servers target `net10.0`.
> **Product version:** **0.1.0 — ALPHA.** Every `.csproj` sets `<Version>0.1.0</Version>`; the README carries a prominent alpha warning that APIs, schemas, config, tool names, and dashboard surfaces will change (breaking, without migration) across 0.1.x. Docker images are **named** (`jchristn77/isis-server`, `jchristn77/isis-mcp`, `jchristn77/isis-dashboard`) and tagged `v0.1.0` (+ `latest`), built via Pneuma-style root scripts: `build.bat`/`test.bat` (local dotnet + dashboard) and `build-server.bat` / `build-mcp.bat` / `build-dashboard.bat` / `build-all.bat` (`docker buildx ... --push`).

---

## Table of Contents

1. [Product Summary](#1-product-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Domain Model](#3-domain-model)
4. [Storage & Search Providers](#4-storage--search-providers)
5. [RecallDB Mapping & Integration](#5-recalldb-mapping--integration)
6. [Model Endpoints (Embedding + Inference) & Health Checks](#6-model-endpoints-embedding--inference--health-checks)
7. [Authentication, Authorization, Multi-Tenancy](#7-authentication-authorization-multi-tenancy)
8. [REST API Surface](#8-rest-api-surface)
9. [MCP Surface (Isis.McpServer)](#9-mcp-surface-isismcpserver)
10. [Dashboard](#10-dashboard)
11. [Telemetry & Observability](#11-telemetry--observability)
12. [Repository Layout](#12-repository-layout)
13. [Settings & Configuration](#13-settings--configuration)
14. [Deployment (Docker + nginx)](#14-deployment-docker--nginx)
15. [Testing Strategy](#15-testing-strategy)
16. [SDKs](#16-sdks)
17. [Build Phases](#17-build-phases)
18. [Value Model](#18-value-model)
19. [Risks & Open Items](#19-risks--open-items)

---

## 1. Product Summary

Isis gives a model durable, structured, queryable **memory** that survives across sessions, harnesses, and projects. It is domain-agnostic: it holds notes about a codebase (what lives where, what a function does, "I did X"), a book, an inbox, a calendar, or **cross-cutting** guidance that applies everywhere (a house writing style, GitHub commit rules, review checklists).

Two surfaces:

- **MCP** (`Isis.McpServer`) — the agent-facing surface. A small, self-describing tool set (`guide`, category CRUD/enumerate, memory CRUD/enumerate/**search**, policy enumerate). The agent calls `guide` first to learn the categories and their usage instructions.
- **REST** (`Isis.Server`) — the human/dashboard-facing management surface. Full CRUD over tenants, scopes, categories, memories, policies, seed packs, model endpoints, plus request history, health, and OpenAPI.

The problem it removes is **re-acquisition of context**: today an agent re-scans a filesystem or re-learns conventions every session, burning tokens on work already done. Isis turns that recurring cost into a one-time write plus cheap recall.

---

## 2. Architecture Overview

Isis is a memory-domain platform layered on **RecallDB** (persistence + hybrid retrieval) with pluggable alternative stores.

```
                         ┌─────────────────────────────────────────────┐
   Agent harness  ──MCP──▶  nginx (mcp)  ──▶  Isis.McpServer (Voltaic)  │
   (Claude Code,          └─────────────────────────────┬───────────────┘
    Cursor, etc.)                        proxies REST    │ 127.0.0.1
                                                         ▼
   Operator/UI  ──HTTPS──▶  nginx (rest) ──▶  Isis.Server (Watson 7.1) ──┐
                          └──────────────────────────────────────────────┘
                                    │                         │
                     Isis metadata  │                         │  memory content + vectors
                     (categories,   ▼                         ▼  (via RecallDb.Sdk over HTTP)
                      instructions, ┌──────────────┐   ┌──────────────────┐
                      policies,     │  Postgres    │   │  RecallDB server │
                      links, slugs, │  db: isis    │   │  :8600 REST      │
                      endpoints,    │  (Isis owns) │   │  db: recalldb    │
                      req-history)  └──────┬───────┘   └────────┬─────────┘
                                           │  same Postgres instance        │
                                           └────────────────────────────────┘
   Embedding endpoint ◀── Isis computes vectors ──┐
   Inference endpoint ◀── Isis summarizes/compacts ┘   (health-checked, dedup by method+URL+auth)
```

Design decisions:

- **RecallDB is the system of record for memory content, embeddings, and retrieval** (per the user's directive). It runs against a shared Postgres instance.
- **Isis owns a separate database (`isis`) on that same Postgres instance** for the memory-domain concepts RecallDB has no representation for: category descriptions/instructions, policies, seed packs, memory links, slugs/titles/summaries, model-endpoint configs, and request history. This honors "one shared Postgres instance, RecallDB is the intelligence store" while respecting RecallDB's actual schema (RecallDB has tenants/collections/documents/labels/tags but no notion of category instructions, policies, or a link graph).
- **Isis reaches RecallDB server-side via the `RecallDbClient` SDK over HTTP** (`http://recalldb-server:8600`), never by sharing RecallDB's tables. Isis holds the admin key / per-tenant credential tokens. Isis does **not** expose RecallDB's own MCP surface to agents (that is an infra/admin surface).
- **The memory store is pluggable** via `IMemoryStore`. RecallDB is the default/primary provider. **Verbex** (text/TF-IDF, no embeddings) and **Filesystem** (single-file or hierarchy) are alternatives, honoring the earlier "database providers AND filesystem providers" requirement.
- **Isis owns embedding + inference generation.** RecallDB is confirmed bring-your-own-vector (no embedding/inference code exists in it). Isis computes embeddings via a configured, health-checked embedding endpoint and passes the float vector to RecallDB; it uses a configured inference endpoint for memory hygiene (summaries, dedup, compaction).
- **MCP is a standalone Voltaic 0.6.1 exe** that authenticates callers and **proxies the Isis REST API** over loopback (Pattern A — Pneuma/AssistantHub). This keeps it stateless/horizontally scalable and reuses the REST server's tenant enforcement, telemetry, and request-history capture.
- **Two nginx instances** front `Isis.Server` and `Isis.McpServer` independently (per the deployment directive).

> **Path convention note:** Isis uses `/v1.0/...` REST paths to match the ecosystem it integrates with (RecallDB and Verbex both use `/v1.0/`). `BACKEND_ARCHITECTURE.md` cites `/api/v1/...` in its API Rules but its own Watson host example uses `/v1.0/api/health`; confirm the house standard before first commit and apply globally. This plan assumes `/v1.0/`.

---

## 3. Domain Model

| Concept | Definition | Home | PrettyId prefix |
|---|---|---|---|
| **Tenant** | Top-level isolation boundary. | Isis DB + RecallDB tenant | `ten_` |
| **User** | Interactive principal within a tenant. | Isis DB | `usr_` |
| **Credential** | Non-interactive principal (API/automation). | Isis DB | `crd_` |
| **Session** | Revocable auth session. | Isis DB | `ses_` |
| **Scope** | A named memory space (a project, a book, or "global"). Maps to one RecallDB collection; fixes one embedding model/dimension. | Isis DB (+ RecallDB collection) | `scp_` |
| **Category** | A named bucket with a **description** and **usage instructions** — the contract the model reads to know when/how to write. Maps to a RecallDB label. | Isis DB (+ label) | `cat_` |
| **Memory** | One atomic note: title, body, summary, tags, type, metadata, links, provenance, salience. | RecallDB document (body+vector) + Isis DB index row | `mem_` |
| **Link** | Typed edge between memories (`[[slug]]` graph). | Isis DB | `lnk_` |
| **Policy** | Always-on cross-cutting guidance (writing style, commit rules). Surfaced proactively via `guide`, not searched. | Isis DB | `pol_` |
| **SeedPack (Profile)** | A user-supplied bundle of categories + instructions + policies that initializes a scope. | Isis DB | `seed_` |
| **EmbeddingEndpoint** | Configured, health-checked embedding model endpoint. | Isis DB | `eep_` |
| **InferenceEndpoint** | Configured, health-checked completion/inference endpoint (memory hygiene + chat). | Isis DB | `iep_` |
| **ChatSession** | A persisted natural-language conversation held against one scope's memory (the "Chat with Memory" surface). | Isis DB | `cht_` |
| **ChatMessage** | One turn in a ChatSession (user question or synthesized answer + cited memory ids). | Isis DB | `cmsg_` |
| **RequestHistoryEntry** | Captured request/response. | Isis DB | `req_` |

### Memory canonical shape (Isis DTO)

```jsonc
{
  "id": "mem_...",                 // Isis PrettyId (index row)
  "tenantId": "ten_...",
  "scopeId": "scp_...",            // -> RecallDB collection
  "categoryId": "cat_...",         // -> RecallDB label
  "slug": "filesystem-layout",     // stable, link-addressable; unique per (scope, category)
  "recallDocumentKey": "...",      // RecallDB DocumentKey (join key)
  "title": "Where things live in the repo",
  "type": "project",               // user|feedback|project|reference (enum, extensible)
  "summary": "One-line recall hook.",   // token-cheap; returned in list/search
  "body": "src/ holds ...",        // stored as RecallDB document Content
  "tags": ["layout"],              // -> RecallDB tags (key/value) + Isis
  "links": ["build-commands"],     // slugs; Isis link graph
  "metadata": { "files": ["src/Foo.cs"], "confidence": 0.8 },
  "salience": 0.72,                // ranking signal, bumped on read
  "provenance": { "author": "agent|human", "sessionId": "...", "model": "claude-opus-4-8" },
  "createdUtc": "...", "updatedUtc": "...", "lastAccessedUtc": "...",
  "version": 3
}
```

`memory_create` is **idempotent on `(scopeId, categoryId, slug)`** — re-writing "filesystem-layout" updates in place rather than duplicating.

---

## 4. Storage & Search Providers

`Isis.Core` defines `IMemoryStore` (the seam), with three implementations. Selection is **per-scope** (`Scope.StoreProvider`).

| Provider | Content SoR | Search | Embeddings needed | Deps |
|---|---|---|---|---|
| **RecallDbMemoryStore** (default) | RecallDB | vector / full-text / **hybrid** | Yes (Isis computes) | RecallDB + Postgres (Docker) |
| **VerbexMemoryStore** | Isis DB (body in Isis) | TF-IDF inverted index | No | Verbex server or in-proc `InvertedIndex` |
| **FilesystemMemoryStore** | Flat files at a target path | DB-native LIKE / optional Verbex | No | none |

`IMemoryStore` (representative surface, all async + `CancellationToken`, `ConfigureAwait(false)`):

```csharp
public interface IMemoryStore
{
    Task EnsureScopeAsync(Scope scope, CancellationToken token);
    Task<Memory> UpsertAsync(Memory memory, float[]? embedding, CancellationToken token);
    Task<Memory?> ReadAsync(string tenantId, string scopeId, string slug, CancellationToken token);
    Task<IReadOnlyList<MemorySummary>> EnumerateAsync(MemoryEnumerationQuery query, CancellationToken token);
    Task<MemorySearchResult> SearchAsync(MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token);
    Task DeleteAsync(string tenantId, string scopeId, string slug, CancellationToken token);
    IAsyncEnumerable<MemorySummary> EnumerateAsyncEnumerable(MemoryEnumerationQuery query, CancellationToken token);
}
```

**FilesystemMemoryStore sub-mode** (`Scope.FilesystemLayout`): `SingleFile` (one document, e.g. `isis-memory.md`, with delimited sections) or `Hierarchy` (a directory tree `<target>/<category>/<slug>.md` + generated `INDEX.md`), both rooted at `Scope.TargetPath`. The hierarchy mode mirrors the frontmatter+index format used by file-based agent memory today (one fact per file, YAML frontmatter, `[[link]]` bodies) so memory is git-trackable and PR-reviewable.

**Provider pattern** for Isis's own metadata DB follows `BACKEND_ARCHITECTURE.md`: `DatabaseDriverBase`, `DatabaseDriverFactory`, provider folders `Sqlite/Mysql/Postgresql/SqlServer` each with `Implementations/` + `Queries/`, domain method interfaces (`ICategoryMethods`, `IMemoryIndexMethods`, `ILinkMethods`, `IPolicyMethods`, `IScopeMethods`, `ISeedPackMethods`, `IEmbeddingEndpointMethods`, `IInferenceEndpointMethods`, `ITenantMethods`, `IUserMethods`, `ICredentialMethods`, `ISessionMethods`, `IRequestHistoryMethods`). Reference: Verbex `Database/`, NetLedger `Database/`.

---

## 5. RecallDB Mapping & Integration

**SDK:** `RecallDb.Sdk` 0.2.1, namespace `RecallDb.Sdk` (client) + `RecallDb.Sdk.Models` (DTOs). Client: `new RecallDbClient(string endpoint, string bearerToken)`. **SDK enum-valued fields are `string`** (e.g. `VectorQuery.SearchType = "CosineSimilarity"`, `DocumentRecord.ContentType = "Text"`) — code against strings.

**Entity mapping:**

| Isis | RecallDB | Notes |
|---|---|---|
| Tenant | `TenantMetadata` (`ten_`) | 1:1. Isis provisions the RecallDB tenant on tenant create. |
| Scope | `CollectionMetadata` (`col_`) | `Dimensionality` = the scope's embedding model dimension (e.g. 1536/3072/1024/384), fixed at creation. |
| Category | RecallDB **label** on documents | Category membership = a label string on each memory doc. Category *instructions* live only in Isis DB. |
| Memory | `DocumentRecord` | `Content`=body, `Embeddings`=vector (len must == collection `Dimensionality`; server validates), `Labels`=[categorySlug, …], `Tags`=metadata (confidence, files, provenance), `DocumentKey`=stable join key, `DocumentId`+`Position` for chunked large memories. |
| Memory search | `SearchAsync(tid, cid, SearchQuery)` | Per-collection = per-scope. Cross-category recall within a scope via label filter; category filter = `LabelFilter.Required`. |

**Write path (`RecallDbMemoryStore.UpsertAsync`):**
1. Isis resolves the scope's embedding endpoint, computes `embedding = EmbeddingService.EmbedAsync(body)`.
2. `RecallDbClient.CreateDocumentAsync(tid, cid, new DocumentRecord { DocumentKey=..., Content=body, Embeddings=embedding.ToList(), Labels=[category], Tags=metadata, ContentType="Text" })` (PUT = upsert; `UpdateDocumentAsync` by key for edits).
3. Isis writes/updates its index row (`IMemoryIndexMethods.UpsertAsync`) with slug, title, summary, links, salience, `recallDocumentKey`.

**Search path (`RecallDbMemoryStore.SearchAsync`):** compute query embedding, build `SearchQuery { Vector = new VectorQuery { SearchType="CosineSimilarity", Embeddings=qvec }, FullText = new FullTextQuery { Query=text, TextWeight=0.5 }, LabelFilter = new LabelFilter { Required=[category?] }, MaxResults=k, IncludeNeighbors=n }`. Hybrid score = `(1-TextWeight)*vectorScore + TextWeight*ftsScore` (single knob `TextWeight`). Return ranked `DocumentRecord`s (each carries `Score`, `Distance`, `TextScore`, `Neighbors`); Isis joins back its index rows for slug/title, then truncates to the caller's `token_budget`.

**Cosine is the natively HNSW-indexed metric** (`vector_cosine_ops`, m=16, ef_construction=64) — prefer `CosineSimilarity`.

**Collection management is pass-through.** RecallDB collection administration in the Isis dashboard (list/create/inspect/delete collections, view stats) is implemented as **thin pass-through proxy calls to RecallDB's own REST API** (`/v1.0/tenants/{tid}/collections...`), not re-implemented in Isis. Isis still owns the scope→collection binding and enforces that a scope's embedding model/dimension is fixed once set (a change requires a new collection + re-embed; the UI blocks silent swaps).

**Shared Postgres:** RecallDB points at db `recalldb`; Isis owns db `isis` on the same instance (recommended, clean logical isolation). Env: `RECALLDB_DB_HOST/PORT/NAME/USER/PASS`, `RECALLDB_DB_SCHEMA`. Admin key `recalldbadmin` (server-side secret only).

**Files to code against:** `sdk/csharp/RecallDb.Sdk/RecallDbClient.cs`, `sdk/csharp/RecallDb.Sdk/Models/*.cs`, `src/RecallDb.Core/Models/SearchQuery.cs`, `src/RecallDb.Core/Database/Postgresql/Implementations/SearchMethods.cs`, `MCP_API.md`, `REST_API.md`.

---

## 6. Model Endpoints (Embedding + Inference) & Health Checks

Isis lets the operator define **embedding** and **inference** endpoints/models via REST + dashboard, and health-checks them. **Copy Conductor** (cleanest, tested, with dedup); optionally lift Partio's `SharedHealthCheckCoordinator` as the standalone dedup engine.

**Models** (Isis DB tables `embedding_endpoints`, `inference_endpoints`), fields adapted from Conductor `ModelRunnerEndpoint` + Partio `EmbeddingEndpoint`:
`Id, TenantId, Name, Kind(Embedding|Inference), ApiFormat(Ollama|OpenAI|vLLM|Gemini), Hostname, Port, UseSsl, ApiKey, Model, Dimensionality(embedding only), TimeoutMs, MaxConcurrentRequests, Weight, Active, Labels, Tags, Metadata,` plus health fields `HealthCheckUrl, HealthCheckMethod(GET|HEAD), HealthCheckIntervalMs(5000), HealthCheckTimeoutMs(5000), HealthCheckExpectedStatusCode(200), HealthyThreshold(2), UnhealthyThreshold(2), HealthCheckUseAuth,` and runtime `ServiceState`. `GetBaseUrl()` = `scheme://host:port`. Format-aware default probe path (Ollama `/api/tags`, Gemini `/v1beta/models`, else `/v1/models`).

**Health check service** (copy Conductor `HealthCheckService.cs`): one loop **per dedup key**, not per endpoint. Dedup key = **method + normalized scheme/host/port/path + SHA256-hashed auth header** (`BuildHealthCheckKey`). Endpoints sharing a key share one probe per cycle (probe with the group's max timeout, fan the single result to all members with hysteresis). Results are in-RAM (`ConcurrentDictionary`), 24h rolling history, exported as `EndpointHealthStatus` (`IsHealthy, LastCheckUtc, Consecutive*, InFlightRequests, LastError, History, UptimePercentage`) and as OpenTelemetry gauges. **Port the dedup unit test** (`HealthCheckServiceDeduplicationTests`: same URL ⇒ 1 probe, different paths ⇒ 2).

**Embedding/inference usage:**
- `EmbeddingService` calls the tenant's active embedding endpoint (format-aware request) to vectorize memory bodies on write and queries on search. Output dimension must match the scope's collection `Dimensionality`.
- `InferenceService` calls the inference endpoint for memory hygiene: generate `summary` on write, semantic dedup suggestions, and `compact` (summarize/merge/prune) operations.
- `MemoryChatService` (the "Chat with Memory" surface) performs retrieval-augmented answering over one scope: embed the user's question, hybrid-search the scope for the top-k relevant memories, compose a grounded prompt (question + retrieved memory bodies + optional prior turns), call the inference endpoint, and return a synthesized answer plus the **cited memory ids** used. Optional streaming. Requires a configured inference endpoint; retrieval quality is best on RecallDB scopes (semantic), and degrades to keyword-only on Verbex/Filesystem scopes (surfaced to the user).

**Do not model on AssistantHub** (no dedup). Files: `Conductor.Server/Services/HealthCheckService.cs`, `Conductor.Core/Models/{ModelRunnerEndpoint,EndpointHealthStatus,EndpointHealthState}.cs`, `Conductor.Server/Routing/ModelRunnerRouteModule.cs`, `Partio.Server/Services/SharedHealthCheckCoordinator.cs`, dashboard `Conductor/dashboard/src/views/ModelRunnerEndpoints.jsx`.

---

## 7. Authentication, Authorization, Multi-Tenancy

Follows `AUTHENTICATION.md` in full. Reference: Verbex, LiteGraph, Armada, Lattice.

- **Tables:** `tenants`, `users` (SHA-256 password, `isadmin`/`istenantadmin`), `credentials` (access key + secret, redacted after create, auth mode enum), `authsessions` (revocable, tenant-bound), `roles`, `userroleassignments` (resource-scoped RBAC), `permissions` (Permit/Deny, resource types incl. `Scope`, `Category`, `Memory`, `Policy`, `SeedPack`, `EmbeddingEndpoint`, `InferenceEndpoint`), `permission` evaluation = **explicit Deny > Permit > implicit Deny**. Every record: string id ≤64, created/updated UTC, `active`.
- **Tenant resolution:** route `/v1.0/tenants/{tenantGuid}/...`, `x-tenant-guid` header, or the authenticated material; all present hints must agree.
- **RequestContext** established in Watson `AuthenticateRequest` hook, stashed in `ctx.Metadata` (reference: LiteGraph `RequestContext.cs`, Lattice `RequestContext.cs`).
- **Normalized request tuple** `(PrincipalType, PrincipalGUID, TenantGUID, ...)` for REST **and** MCP.
- **MCP** authenticates via `x-api-key` (admin bootstrap) or `x-tenant-guid` + bearer/`x-token`, building `McpAuthenticatedRequestContext` (see §9). Tenant is enforced in the service layer, never trusted from a tool argument.
- **Prove it:** port Armada `MultiTenantScopingTests` to assert enumeration never leaks cross-tenant rows and resource-scoped grants don't apply to other GUIDs.

---

## 8. REST API Surface

Watson 7.1, per-feature route registrars calling `server.Routes.{Pre,Post}Authentication.{Static,Parameter}.Add(...)`; typed DTOs; explicit status codes; `ctx.Token` threaded through; `Server.UseOpenApi()`; Preflight + PostRouting + CORS. **Every route carries `OpenApiRouteMetadata`**, and `openapi.json` + a Swagger UI route are exposed (dashboard API Explorer depends on them).

```
GET    /v1.0/health
GET    /v1.0/server/info
GET    /v1.0/openapi.json
GET    /v1.0/swagger                         # Swagger UI

# auth
POST   /v1.0/authenticate
POST   /v1.0/tokens        DELETE /v1.0/tokens/{id}

# administration (AUTHENTICATION.md)
.../tenants  .../tenants/{tid}/users  .../credentials  .../roles  .../permissions  .../assignments  .../audit

# scopes
GET|POST /v1.0/tenants/{tid}/scopes
GET|PUT|DELETE /v1.0/tenants/{tid}/scopes/{sid}
POST   /v1.0/tenants/{tid}/scopes/{sid}/seed          # apply a SeedPack
POST   /v1.0/tenants/{tid}/scopes/{sid}/compact       # summarize/dedupe/prune (inference)
GET    /v1.0/tenants/{tid}/scopes/{sid}/export        # to flat-file bundle
POST   /v1.0/tenants/{tid}/scopes/{sid}/import

# categories
GET|POST /v1.0/tenants/{tid}/scopes/{sid}/categories
GET|PUT|DELETE .../categories/{cid}

# memories
GET    .../scopes/{sid}/memories?category=&tag=&limit=&continuationToken=   # summaries only
POST   .../scopes/{sid}/memories                                            # upsert by (scope,category,slug)
GET|PUT|DELETE .../memories/{mid}
POST   .../scopes/{sid}/memories/search       # body: query, tokenBudget, filters, weights
GET    .../memories/{mid}/related             # link graph walk
POST   .../memories/{mid}/links               # assert typed edge

# chat with memory (natural-language RAG over a scope)
POST   /v1.0/tenants/{tid}/scopes/{sid}/chat          # body: question, history?, topK?, stream? -> answer + citations
GET|POST /v1.0/tenants/{tid}/scopes/{sid}/chat/sessions          # list / create persisted sessions
GET|DELETE .../chat/sessions/{chid}                             # read transcript / delete
POST   .../chat/sessions/{chid}/messages              # ask within a persisted session -> answer + citations

# policies & seed packs
GET|POST /v1.0/tenants/{tid}/policies         GET|PUT|DELETE .../policies/{pid}
GET|POST /v1.0/tenants/{tid}/seedpacks        GET|PUT|DELETE .../seedpacks/{spid}

# model endpoints (Conductor-style)
GET|POST /v1.0/tenants/{tid}/endpoints/embedding    GET|PUT|DELETE|HEAD .../embedding/{id}
GET      .../endpoints/embedding/health             GET .../embedding/{id}/health
POST     .../endpoints/embedding/{id}/test          # one-shot probe
(identical surface under /endpoints/inference)

# guide (agent onboarding manifest)
GET    /v1.0/tenants/{tid}/scopes/{sid}/guide        # categories+instructions+active policies+examples

# observability
GET    /v1.0/requests   GET /v1.0/requests/{id}   DELETE /v1.0/requests
```

Route registrars: `HealthRoutes, AuthRoutes, TenantRoutes, UserRoutes, CredentialRoutes, RoleRoutes, PermissionRoutes, ScopeRoutes, CategoryRoutes, MemoryRoutes, PolicyRoutes, SeedPackRoutes, EmbeddingEndpointRoutes, InferenceEndpointRoutes, GuideRoutes, RequestHistoryRoutes`. Request capture/history per `BACKEND_ARCHITECTURE.md` (reference: Lattice `RequestHistoryService.cs`, Pneuma `AuditAndRequestHistoryRoutes.cs`).

---

## 9. MCP Surface (Isis.McpServer)

**Standalone `Exe`, Voltaic 0.6.1, Pattern A** (authenticate + proxy REST over `127.0.0.1`). Reference: Pneuma-Old `Pneuma.McpServer` (`McpAuthenticatedRequestContext.cs`) and AssistantHub `AssistantHub.McpServer` (Voltaic 0.6.1).

**Host** (`IsisMcpServer.cs`): `new McpHttpServer(Mcp.Hostname, Mcp.Port, Mcp.RpcPath, Mcp.EventsPath, includeDefaultMethods:true, Mcp.McpPath)`; set `ServerName="Isis.McpServer"`, `AuthenticationHandler = AuthenticateAsync`, `RequestReceived += OnRequestReceived`. Transport = Streamable HTTP at `/mcp` (+ `/rpc`, `/events`). `includeDefaultMethods:true` auto-registers `ping`/`echo`/`getTime`/`getSessions`/`tools/list`/`tools/call`.

**Auth bridge:** Voltaic does not pass auth into handlers. Replicate Pneuma's bridge — set an `AsyncLocal<McpAuthenticatedRequestContext>` inside `AuthenticateAsync`, and use the `RequestReceived` event + a pending-context map keyed by tool+args so the correct tenant context is retrieved inside each handler under SSE. `McpAuthenticatedRequestContext` = `{ PrincipalId, PrincipalType, TenantId, ScopeId, BootstrapApiKeyAuthenticated, UseBearerToken, PresentedToken }`.

**REST proxy:** one `HttpClient` with `BaseAddress = http://127.0.0.1:{Rest.Port}` (normalize wildcard host to `127.0.0.1` — avoids the Windows `localhost`→`::1` stall). `InvokeRestToolAsync(tool, method, route, args, includeBody, ct)` forwards the caller's credentials downstream and wraps responses in `{ success, statusCode, tool, data|error }`.

**Tools (`` prefix, ~15), one class per group in `Tools/`:**

| Tool | Purpose | Proxies |
|---|---|---|
| `guide` | **Call first.** Returns categories + instructions + active policies + worked examples. | GET `/guide` |
| `health` | Liveness. | GET `/health` |
| `policy_enumerate` | Always-on guidance. | GET `/policies` |
| `category_enumerate` | Scope categories incl. instructions. | GET `/categories` |
| `category_read` / `_create` / `_update` / `_delete` | Category CRUD. | `/categories[/{id}]` |
| `memory_enumerate` | **Summaries only** (token-cheap), filter by category/tag. | GET `/memories` |
| `memory_search` | Keyword+semantic; ranked snippets under `token_budget`. | POST `/memories/search` |
| `memory_read` | Full body by id/slug (only call returning full text). | GET `/memories/{id}` |
| `memory_create` | Idempotent upsert by `(scope,category,slug)`; returns slug. | POST `/memories` |
| `memory_update` / `_delete` | Edit / remove. | `/memories/{id}` |

Schemas built with a copied `CreateSchema(bool additionalProperties, params McpSchemaProperty[])` + `RequiredString/OptionalString/OptionalBoolean/OptionalInteger/OptionalStringArray` helpers (Pneuma `Infrastructure.cs`). `tenantId`/`scopeId` come from the request context, not tool args, so a model cannot spoof another tenant. An `install` verb writes the caller's `~/.claude.json` `mcpServers` entry pointing at `http://host:port/mcp` (AssistantHub pattern).

**csproj:** `OutputType=Exe`, `TargetFrameworks net8.0;net10.0`, `PackageReference Voltaic 0.6.1`, `ProjectReference Isis.Core`. (Voltaic ≥1.0 renames core types — stay on 0.6.1.)

---

## 10. Dashboard

React 19 / Vite 6 / React Router 7, browser `fetch` behind a shared `ApiClient`, hand-rolled SVG charts (no chart lib), i18next foundation. Complies with `FRONTEND_ARCHITECTURE.md` + `DASHBOARD_STYLE_AND_USABILITY.md` (full first pass, no "coming soon" placeholders, mandatory Home/Request History/API Explorer/Settings, role-aware nav, dark/light + desktop/tablet/mobile verified). Reference dashboards: **Conductor** (endpoint + health views), **Verbex** (indices/documents/search), **Lattice** (request history + API explorer), **RecallDB** (collections/documents drill-downs).

**Route inventory (built before coding):**

| Group | Routes |
|---|---|
| **Start** | `/dashboard/home` (KPIs: memories, scopes, categories, searches, embedding-endpoint health, recall hit-rate; activity chart; CTA: create scope, add endpoint, open explorer) |
| **Memory** | `/scopes`, `/scopes/{id}` (drill-down), `/scopes/{id}/categories`, `/scopes/{id}/memories` (table: create/view/edit/delete/view-JSON), `/scopes/{id}/memories/{mid}` (detail + link graph) |
| **Recall** | `/search` (hybrid search explorer: query, weight slider `TextWeight`, label/tag filters, neighbors, ranked results with scores); **`/scopes/{id}/chat` — "Chat with Memory"**: pick a scope, ask its memory questions in natural language, see a streamed synthesized answer with clickable memory **citations**; conversation history persists as ChatSessions and can be resumed. Shows a clear notice when no inference endpoint is configured, or when the scope's store (Verbex/Filesystem) supports keyword-only retrieval. |
| **Govern** | `/policies`, `/seedpacks` (editor) |
| **Inference** | `/endpoints/embedding`, `/endpoints/inference` (CRUD + live health poll every 15s, test modal, health histogram) |
| **Collections (RecallDB)** | `/collections` — **pass-through** to RecallDB's REST API (list/create/inspect/delete + stats); embedding dimension shown read-only per collection |
| **Observability** | `/request-history` (KPIs, chart, filters, inspector modal), external-services card on Home (Grafana/Prometheus/Tempo/RecallDB console URLs + default creds) |
| **API** | `/api-explorer` (OpenAPI-driven from `/v1.0/openapi.json`) |
| **System** | `/settings` (endpoint, version, auth context, feature flags, copyable URLs) |
| **Administration** | `/tenants`, `/users`, `/credentials`, `/roles`, `/permissions`, `/audit` |

Shared components before pages: `Dashboard` shell (sidebar 220–260px, topbar 52–64px), `DataTable`, `Pagination`, `FilterBar`, `Modal`, `Toast`, `ActivityChart`, `HealthHistogram`, `CopyableId`, `RequestDetailsModal`. Structure per `dashboard/src/{views,components,context,hooks,utils,i18n}`.

---

## 11. Telemetry & Observability

Per `TELEMETRY_REQUIREMENTS.md`. Watson 7.1 built-in telemetry enabled (`Settings.Telemetry`, `Watson` meter + activity source); a collector exports to Prometheus + Tempo. Application meters: memory read/write/search counts, **tokens-served-per-response estimate**, recall hit-rate & salience, embedding/inference latency, endpoint health gauges (`isis.health.endpoints.healthy/unhealthy/total`, `isis.health.inflight.requests`). Loki included (background work: health loops, compaction).

`compose.yaml` observability services (pin versions): Prometheus `:9090`, Tempo `:3200/4317/4318`, Grafana `:3000`, Loki `:3100`, Alloy. Grafana provisioned as code with a stable `prometheus`/`tempo` datasource UID and an `Isis` folder of domain dashboards: **Overview, HTTP, Memory Ops (write/search/compact), Inference (embedding/inference latency + health), Integrations (RecallDB/Verbex)**. Dashboard JSON in `assets/grafana/`. Home-page external-services card lists browser-reachable URLs + dev creds (Grafana admin/admin, RecallDB console `:8601`), copyable, degrading gracefully. Never put ids/secrets in metric labels.

---

## 12. Repository Layout

```
AgentMemory/                                  (repo root; product name Isis)
├── src/
│   ├── Isis.sln
│   ├── Isis.Core/
│   │   ├── Constants.cs
│   │   ├── Database/  (DatabaseDriverBase, DatabaseDriverFactory, DatabaseSettings, DatabaseTypeEnum,
│   │   │              SchemaMigration, Interfaces/, Sqlite/ Mysql/ Postgresql/ SqlServer/ {Implementations,Queries})
│   │   ├── Stores/    (IMemoryStore, RecallDb/, Verbex/, Filesystem/{SingleFile,Hierarchy})
│   │   ├── Recall/    (IEmbeddingService, IInferenceService, EmbeddingService, InferenceService)
│   │   ├── Health/    (HealthCheckService, HealthCheckKey, EndpointHealthStatus)  ← Conductor port
│   │   ├── Enums/  Helpers/(IdGenerator.cs)  Models/  Requests/  Responses/  Security/  Serialization/
│   │   └── Services/  (Interfaces/, Implementations/: Scope/Category/Memory/Policy/SeedPack/Compaction)
│   ├── Isis.Server/          (REST — Watson 7.1)
│   │   ├── Program.cs  IsisServer.cs  Settings/  Routes/  Services/  Middleware/  Serialization/
│   ├── Isis.McpServer/       (MCP — Voltaic 0.6.1, Exe)
│   │   ├── Program.cs  IsisMcpServer.cs  McpAuthenticatedRequestContext.cs  Classes/  Tools/
│   ├── Test.Shared/  Test.Automated/  Test.Xunit/  Test.Nunit/
├── dashboard/                (React 19 / Vite 6)
├── sdk/  (csharp/  python/  js/  — each with README + test harness, 127.0.0.1 base URLs)
├── docker/  (compose.yaml, compose.factory.yaml, server/Dockerfile, mcp/Dockerfile,
│            nginx/rest.conf, nginx/mcp.conf, prometheus.yaml, tempo.yaml, grafana/, factory/templates/)
├── assets/  (icon.png, logo.svg, grafana/)
├── docs/  (this plan, provider setup, migration strategy)
├── migrations/
├── .gitignore  .dockerignore  README.md  DOCKERHUB_README.md  CHANGELOG.md  LICENSE.md(MIT)  isis.json
```

One class/enum per file; usings inside namespace; `_PascalCase` privates; no `var`; no tuples; `ConfigureAwait(false)`; `CancellationToken` on every async + `IEnumerable`/async-variant pairs; XML docs on public surface (per `CODE_STYLE.md`).

---

## 13. Settings & Configuration

`isis.json` (JSON, env overrides `ISIS_*`), strongly typed, validated on load, secrets via env:

```jsonc
{
  "Rest":   { "Hostname": "127.0.0.1", "Port": 8700, "Ssl": false },
  "Mcp":    { "Hostname": "127.0.0.1", "Port": 8720, "RpcPath": "/rpc", "EventsPath": "/events", "McpPath": "/mcp" },
  "Database": { "Type": "Postgresql", "Server": "postgres", "Port": 5432, "DatabaseName": "isis",
                "Username": "isis", "Password": "override-via-env" },
  "RecallDb": { "Endpoint": "http://recalldb-server:8600", "AdminApiKey": "override-via-env" },
  "Verbex":   { "Endpoint": null },
  "Logging":  { "ConsoleLogging": true, "FileLogging": true, "LogDirectory": "logs", "LogFilename": "isis.log" },
  "Telemetry":{ "Enable": true, "OtlpEndpoint": "http://tempo:4317" },
  "Auth":     { "Issuer": "isis", "SigningKey": "override-via-env", "BootstrapApiKey": "override-via-env" }
}
```

Env overrides: `ISIS_SETTINGS_FILE`, `ISIS_DB_TYPE/SERVER/PORT/DATABASE/USERNAME/PASSWORD`, `ISIS_RECALLDB_ENDPOINT`, `ISIS_RECALLDB_ADMIN_KEY`, `ISIS_AUTH_SIGNING_KEY`, `ISIS_AUTH_BOOTSTRAP_KEY`, `ISIS_MCP_PORT`, `ISIS_REST_PORT`.

---

## 14. Deployment (Docker + nginx)

`docker/compose.yaml` (`.yaml`, **named images** `jchristn77/isis-*:v0.1.0` — not anonymous build contexts — with `build:` stanzas for local rebuilds, `127.0.0.1` loopback), one command up. Images are produced by the root `build-*.bat` scripts (`docker buildx ... --push`, multi-arch amd64+arm64), matching the Pneuma convention. Services:

| Service | Image/Build | Ports | Role |
|---|---|---|---|
| postgres | `ankane/pgvector:*` (pinned) | 5432 | shared DB (`isis` + `recalldb` databases) |
| recalldb-server | `jchristn77/recalldb-server:v0.2.1` | 8600, 8620 | memory content + hybrid search |
| recalldb-dashboard | `jchristn77/recalldb-dashboard:v0.2.1` | 8601 | RecallDB admin console (ops only) |
| isis-server | `jchristn77/isis-server:v0.1.0` (build `docker/server/Dockerfile`) | 8700 | REST |
| isis-mcp | `jchristn77/isis-mcp:v0.1.0` (build `docker/mcp/Dockerfile`) | 8720 | MCP |
| isis-dashboard | `jchristn77/isis-dashboard:v0.1.0` (build `dashboard/Dockerfile`) | 8701 | dashboard |
| nginx-rest | `nginx:*` + `nginx/rest.conf` | 443/80 | fronts isis-server |
| nginx-mcp | `nginx:*` + `nginx/mcp.conf` | 8443 | fronts isis-mcp |
| prometheus / tempo / loki / alloy / grafana | pinned | 9090 / 3200,4317,4318 / 3100 / 12345 / 3000 | observability |

Two nginx instances (per directive) with independent configs so REST and MCP scale/TLS-terminate separately; MCP nginx must pass through SSE/streamable-HTTP (`proxy_buffering off`, upgrade headers). `compose.factory.yaml` seeds a demo tenant/scope/categories/policies. Postgres init creates both `isis` and `recalldb` databases; Isis runs its own idempotent migrations; RecallDB provisions pgvector/pg_trgm itself. Gate Grafana on Prometheus/Tempo health; change Grafana creds out of band for non-local.

---

## 15. Testing Strategy

Touchstone shared-suite + multi-runner per `BACKEND_TEST_ARCHITECTURE.md`: `Test.Shared` (`IsisSuites.cs` + `Suites/`, `Touchstone.Core` only, no console output), `Test.Automated` (`Touchstone.Cli`), `Test.Xunit`, `Test.Nunit`. `TargetFrameworks net8.0;net10.0`. Reference: Conductor `Test.Shared/Touchstone/SharedTestSuites.cs`, Tempo `Suites/`.

**Suites:** identifiers/PrettyId, security (auth/RBAC/permit-deny), tenant scoping (**port Armada `MultiTenantScopingTests`** — no cross-tenant leakage, resource-scope enforcement), scopes, categories (+ instructions), memory upsert/idempotency-by-slug, link graph, **store provider matrix** (RecallDB / Verbex / Filesystem single-file / Filesystem hierarchy), **Isis DB provider matrix** (Sqlite/Mysql/Postgresql/SqlServer), search (vector/full-text/hybrid weighting, token-budget truncation), **health-check dedup** (port Conductor `HealthCheckServiceDeduplicationTests`: same URL⇒1 probe, different paths⇒2), embedding/inference service (mock endpoints), request history, MCP tools (live in-process `McpHttpServer`, auth + per-tenant context + each tool). RecallDB-backed suites run against the compose Postgres+RecallDB (integration category, skippable when unavailable with a logged gap, never silently).

---

## 16. SDKs

`sdk/csharp`, `sdk/python`, `sdk/js` — each a typed client over the REST API with README + test harness, loopback base URL `127.0.0.1`. C# SDK builds a NuGet package with symbols, README, icon, license (per `REPOSITORY_REQUIREMENTS.md` NuGet rules). Reference: Verbex `sdk/`, SharpAI `sdk/csharp`. Client method names: `EnumerateScopesAsync`, `UpsertMemoryAsync`, `SearchMemoriesAsync`, `EnumerateCategoriesAsync`, `GetGuideAsync`, etc.

---

## 17. Build Phases

1. **Core foundations** — `Isis.Core` models/enums/PrettyId/`IdGenerator`, Isis DB provider abstraction (Sqlite first), auth (tenant/user/credential/session/RBAC), migrations, request history. Tests: security + tenant scoping.
2. **RecallDB store + memory/category services + REST** — `RecallDbMemoryStore`, `EmbeddingService`, scope→collection provisioning, category/memory/scope routes, `Server.UseOpenApi()` + `openapi.json`/swagger, guide route. Tests: store matrix (RecallDB), search.
3. **Model endpoints + health checks** — embedding/inference endpoint CRUD + Conductor `HealthCheckService` (dedup) + inference-driven summaries/compaction. Tests: health dedup.
4. **Isis.McpServer (Voltaic 0.6.1)** — Pattern A host, auth bridge, ~15 tools, `install` verb. Tests: MCP tool suite.
5. **Alternative stores** — Verbex + Filesystem (single-file/hierarchy) providers; policies + seed packs + export/import. Tests: full store matrix.
6. **Dashboard (full first pass)** — shell, all route groups, endpoint health views, search explorer, request history, API explorer, admin.
7. **Remaining DB providers (Mysql/Postgres/SqlServer) + SDKs + Docker/nginx + observability stack + DOCKERHUB packaging + i18n.**

---

## 18. Value Model

The dominant saved cost is **context re-acquisition**. Let `S` = tokens to understand a workspace cold (50k–300k+), `R` = tokens to recall from Isis (a `guide` call + a few summary/snippet hops, ~1k–8k), `W` = one-time write cost (~2k–10k, mostly riding work already happening), `N` = sessions/tasks reusing the context.

- **Without memory:** `N × S`. **With memory:** `S + W + (N × R)`.
- For `S=150k, R=5k, W=8k, N=10`: `1,500k → 208k` tokens — **~86% reduction** in acquisition cost; **break-even at N≈2**, compounding after.
- **Latency:** time-to-first-useful-action drops from "scan the repo" to one hybrid-search round trip (tens of ms server-side); summaries-first ⇒ shorter agent turns.
- **Throughput:** less window spent on re-acquisition ⇒ more room for the actual task, fewer compaction cycles; stateless REST/MCP scale horizontally; recall is read-heavy (suits caching/read replicas).
- **Cross-project leverage:** over-arching policies (style, commit rules) are written once and recalled everywhere, so `N` spans projects, not just sessions.
- **Where it loses:** one-shot tasks (`N=1`) don't amortize `W`; a stale/wrong memory is worse than none. Mitigations are first-class: provenance + timestamps, salience decay, confidence metadata, and the inference-driven `compact` operation.

---

## 19. Risks & Open Items

- **REST path convention** (`/v1.0/` vs `/api/v1/`) — resolve against the house standard before first commit (§2 note).
- **Embedding dimension is fixed per RecallDB collection** — changing a scope's embedding model requires a new collection + re-embed/migrate. Surface this in the scope UI; block silent model swaps.
- **RecallDB SDK enum-as-string** — code against string literals; add a constants class to avoid typos.
- **RecallDB SDK publication** — confirm `RecallDb.Sdk` 0.2.1 is on nuget.org or vendor via `ProjectReference`.
- **`.NET 10` floor** — all projects target net10.0 (libs multi-target); confirm CI runners have the SDK.
- **MCP auth bridge under SSE** — the `AsyncLocal` + `RequestReceived` correlation is subtle; port Pneuma's implementation verbatim and cover it with the MCP test suite.
- **Policy surfacing** — policies surface via `guide` (pull), not silent injection; confirm this matches operator expectation.
- **Voltaic version pin** — stay on 0.6.1; ≥1.0 renames `RpcParameters`/`McpHttpServer`/`ClientConnection`.
```
