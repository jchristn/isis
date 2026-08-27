# Changelog

All notable changes to Isis are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0] - ALPHA (in progress)

### Added

- **Authentication overhaul — email/password sessions + two-key credentials.** Removed the admin
  `x-api-key` bootstrap scheme entirely. There are now exactly two mechanisms: (1) **email + password
  → session token** for interactive users and the dashboard — `POST /v1.0/api/tenants-for-email`
  lists the tenants an email belongs to, `POST /v1.0/api/token` issues a bearer token (sent as
  `Authorization: Bearer <token>` or `x-token`), `GET /v1.0/api/whoami` resolves the principal, and
  `DELETE /v1.0/api/token` revokes the session (logout); and (2) **credential access key + secret
  key** for automation, MCP, and agents — callers now send **both** `x-access-key` and `x-secret-key`
  (the secret is newly required; the access key alone no longer authenticates). Administrative power
  comes only from the user record's `IsAdmin` (system-wide) or `IsTenantAdmin` (tenant-wide) flags —
  there is no admin key or admin principal. Added user and credential management REST endpoints and
  dashboard views. First-boot seeding now creates a bootstrap admin user (`admin@isis.local` /
  `isisadmin`, env `ISIS_AUTH_SEED_ADMIN_EMAIL` / `ISIS_AUTH_SEED_ADMIN_PASSWORD`) in tenant
  `ten_default` and a default credential (`isisdefaultkey` / `isisdefaultsecret`, env
  `ISIS_AUTH_DEFAULT_ACCESS_KEY` / `ISIS_AUTH_DEFAULT_SECRET_KEY`). The MCP installer's flags are now
  `--access-key` / `--secret-key` and it writes both headers; the MCP 401 message is now
  `Provide x-access-key and x-secret-key headers.`
- Initial repository scaffolding: `README.md` (with the backing-store capability matrix and alpha
  warning), `LICENSE.md` (MIT), `.gitignore`, `.dockerignore`, `isis.json`, and Pneuma-style build
  scripts (`build.bat`, `test.bat`, `build-server.bat`, `build-mcp.bat`, `build-dashboard.bat`,
  `build-all.bat`).
- Product plan (`docs/ISIS_PLAN.md`), including the "Chat with Memory" surface and RecallDB
  pass-through collection management.
- `Isis.Core` (builds clean; runtime-verified end to end):
  - Constants, PrettyId identifier generation, and domain enums.
  - Domain models: tenants, users, credentials, auth sessions, scopes, categories, memories, memory
    links (plus enumeration query/result types).
  - Provider-neutral database abstraction (`DatabaseDriverBase`, `DatabaseDriverFactory`) with a
    complete **SQLite** provider for tenants, users, credentials, sessions, scopes, categories, and
    the memory index.
  - Pluggable `IMemoryStore` seam with a working **filesystem** provider (single-file and hierarchy
    layouts, keyword search), and capability-accurate RecallDB/Verbex providers (integration wired in
    a later phase).

- `Isis.Server` (REST, Watson 7.1 — runs and is tested end to end):
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
- `Isis.McpServer` (Voltaic 0.6.1, standalone — runs and is tested):
  - Streamable-HTTP MCP transport that speaks the MCP `initialize` handshake and hosts 10 agent tools
    (`isis_whoami`, `isis_scope_enumerate`, `isis_guide`, `isis_category_enumerate`/`_create`,
    `isis_memory_enumerate`/`_read`/`_upsert`/`_search`/`_delete`).
  - Authenticates the caller from transport headers (`x-access-key` + `x-secret-key`) and proxies each
    tool to the Isis REST API over loopback, forwarding the caller's credentials so REST performs the
    authoritative auth and tenant scoping.
- Model endpoints + health checking:
  - `ModelEndpoint` (embedding/inference) with persistence (SQLite `model_endpoints` table,
    `IModelEndpointMethods`), tenant-scoped CRUD REST routes under `/v1.0/api/tenants/{id}/endpoints`,
    and kind-correct ids (`eep_` / `iep_`).
  - `HealthCheckService` that probes endpoints and **deduplicates by method + normalized URL + hashed
    auth**, applying one probe result to all endpoints sharing a target, with healthy/unhealthy
    threshold hysteresis. Live probe route at `/v1.0/api/tenants/{id}/endpoint-health`.
- Embedding + inference + chat-with-memory:
  - `EmbeddingService` and `InferenceService` (`Isis.Core.Recall`) that call configured model endpoints
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
  with named `jchristn77/isis-*:v0.1.0` images + build stanzas, shared pgvector Postgres (init creates
  `isis` + `recalldb` databases), RecallDB server/dashboard, two nginx instances (REST + MCP with SSE
  passthrough), and the pinned observability stack (Prometheus/Tempo/Loki/Alloy/Grafana); Dockerfiles
  for server/mcp/dashboard; Grafana provisioning + an Overview dashboard; a factory/demo overlay; and
  `DOCKERHUB_README.md`. New env overrides: `ISIS_REST_HOSTNAME`, `ISIS_DB_PORT`, `ISIS_RECALLDB_ENDPOINT`,
  `ISIS_RECALLDB_ADMIN_KEY`.
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
- **`isis mcp install`** command in `Isis.McpServer`: upserts an `isis` MCP entry (`type: http`,
  `url: http://127.0.0.1:8720/mcp`, `x-access-key` + `x-secret-key` headers) into the agent client
  config (`~/.claude.json` or a project `.mcp.json` with `--project`), preserving all other servers and
  keys, writing a `.bak` backup. Flags: `--access-key`/`--secret-key`/`--host`/`--port`/`--url`/`--project`;
  reads defaults from `isis.mcp.json` + env.
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
