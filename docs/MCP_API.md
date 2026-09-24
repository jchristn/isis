# Isis MCP API

> **Naming.** The product is **Isis** — a proper noun, written `Isis` or `isis`. It is **not**
> an acronym; never write it as the all-caps `ISIS`.
>
> **Tool names.** Tools are registered **without** an `isis_` prefix (`whoami`, `memory_upsert`,
> `scope_create`, …). Your MCP client namespaces them under the server key you configure it with
> (conventionally `isis`), so you will see them as e.g. `isis.whoami` / `mcp__isis__whoami` — a
> single, clean namespace, not a doubled `isis_isis_*`.
>
> **tenantId.** Every tool except `whoami` requires a `tenantId` argument. Call `whoami` first; its
> response gives you the `tenantId` to pass to all other calls.

Isis exposes an HTTP MCP server for AI agents. The MCP endpoint is:

```text
http://localhost:8720/mcp
```

The transport is **streamable HTTP + Server-Sent Events (SSE)**. The same server also
exposes the classic JSON-RPC path `/rpc` and the SSE events path `/events`; MCP clients
should use `/mcp`. The MCP host, port, and paths are configured in `isis.mcp.json`
(`Hostname`, `Port`, `RpcPath`, `EventsPath`, `McpPath`) and can be overridden with the
`ISIS_MCP_HOSTNAME` and `ISIS_MCP_PORT` environment variables.

The Isis MCP server is a thin, stateless front end: it authenticates the caller from the
transport headers and **proxies each tool call to the Isis REST API** over loopback,
forwarding the caller's credentials so the REST server performs the authoritative
authentication and tenant scoping.

## Security Model

Every MCP request must present a credential **access key**. The access key alone
authenticates the caller and identifies the tenant credential; the secret key is **optional**.
Present the access key one of two ways:

| Auth material | How to send it | Default value | Notes |
|---------------|----------------|---------------|-------|
| Access key (bearer) | `Authorization: Bearer <accessKey>` | `isisdefaultkey` | Single-header clients (e.g. Mux) use this |
| Access key (header) | `x-access-key: <accessKey>` | `isisdefaultkey` | Equivalent alternative to the bearer token |
| Secret key (optional) | `x-secret-key: <secretKey>` | `isisdefaultsecret` | Validated **only if present**; never required |

The MCP server accepts either the `Authorization: Bearer <accessKey>` token or the
`x-access-key` header, and honors an `x-secret-key` header only when one is supplied. A
request that presents **no access key** is rejected with HTTP `401` before any tool runs. The
access key is **public and transferable** — it authenticates on its own, so treat it as a
**capability token** and prefer a least-privilege credential. The `x-secret-key` header is a
legacy/optional extra: the server still validates it when present, but no agent installer
(Claude Code, Codex, Cursor, Gemini, Mux) sends one — the secret never leaves the client.

The access key (and the secret key, when present) is forwarded verbatim to the Isis REST API,
which enforces tenant isolation. Tenant identity is never trusted from a tool argument alone;
the REST layer validates that the caller's credential is authorized for the `tenantId` it
operates on. Administrative power, when a credential's user has it, comes from the user
record's `IsAdmin` (system-wide) or `IsTenantAdmin` (tenant-wide) flags — there is no separate
admin key.

Change the default keys before exposing Isis outside a trusted local environment.

## Response Envelope

Every tool returns the same structured envelope. The proxied REST response is embedded
under `data`.

| Field | Type | Description |
|-------|------|-------------|
| `tool` | string | The tool name, echoed back |
| `success` | boolean | `true` when the proxied REST call returned a 2xx status |
| `statusCode` | integer | The HTTP status code returned by the Isis REST API |
| `data` | object, array, string, or null | The REST response body, parsed as JSON when possible |

```json
{
  "tool": "whoami",
  "success": true,
  "statusCode": 200,
  "data": {
    "tenantId": "ten_a1b2c3",
    "principalType": "Credential",
    "principalName": "default",
    "credentialId": "crd_9x8y7z"
  }
}
```

When the REST call fails, `success` is `false`, `statusCode` carries the upstream code
(for example `401`, `403`, `404`), and `data` contains the REST error body.

## Tool Inventory

Isis exposes **32** MCP tools at parity with the tenant-scoped REST surface. Each tool proxies the
REST route shown, forwarding the caller's credential; the write/CRUD tools accept the same fields as
the corresponding REST request body. (Voltaic also auto-registers `ping`, `echo`, `getTime`, and
`getSessions`.)

| Tool | REST route proxied | Purpose |
|------|--------------------|---------|
| `whoami` | `GET /whoami` | Resolve the tenant and principal the caller's credential maps to |
| `instructions` | `GET .../instructions` or `.../scopes/{sid}/effective-instructions` | Standing instructions; pass `scopeId` for a scope's effective (merged) set |
| `guide` | `GET .../scopes/{sid}/guide` | A scope's categories, usage instructions, and store capabilities |
| `scope_enumerate` | `GET .../scopes` | List the memory scopes in a tenant |
| `scope_create` | `POST .../scopes` | Create a memory scope |
| `scope_read` | `GET .../scopes/{sid}` | Read a scope by id |
| `scope_update` | `PUT .../scopes/{sid}` | Update a scope's name/description |
| `scope_delete` | `DELETE .../scopes/{sid}` | Delete a scope (cascades categories, memories, scope instructions) |
| `category_enumerate` | `GET .../categories` | List categories in a scope |
| `category_create` | `POST .../categories` | Create a category |
| `category_read` | `GET .../categories/{cid}` | Read a category by id |
| `category_update` | `PUT .../categories/{cid}` | Update a category |
| `category_delete` | `DELETE .../categories/{cid}` | Delete a category |
| `memory_enumerate` | `GET .../memories` | List memory summaries in a scope (token-cheap) |
| `memory_read` | `GET .../memories/{mid}` | Read a single memory by id (full body) |
| `memory_upsert` | `POST .../memories` | Create or update a memory; idempotent on `(scope, category, slug)` |
| `memory_search` | `POST .../memories/search` | Search a scope's memory (keyword, semantic, or hybrid) |
| `memory_delete` | `DELETE .../memories/{mid}` | Delete a memory by id |
| `endpoint_enumerate` | `GET .../endpoints` | List a tenant's model endpoints |
| `endpoint_read` | `GET .../endpoints/{eid}` | Read a model endpoint by id |
| `endpoint_create` | `POST .../endpoints` | Create a model endpoint (base URL + auth) |
| `endpoint_update` | `PUT .../endpoints/{eid}` | Update a model endpoint |
| `endpoint_delete` | `DELETE .../endpoints/{eid}` | Delete a model endpoint |
| `endpoint_health` | `GET .../endpoint-health` | Probe and return endpoint health |
| `chat` | `POST .../scopes/{sid}/chat` | Ask a question answered from a scope's memory (RAG); returns answer + citations |
| `collection_enumerate` | `GET .../collections` | List the RecallDB collections backing scopes |
| `collection_read` | `GET .../collections/{cid}` | Read a RecallDB collection by id |
| `collection_create` | `POST .../collections` | Create a RecallDB collection directly |
| `collection_delete` | `DELETE .../collections/{cid}` | Delete a RecallDB collection |
| `instruction_create` | `POST .../instructions` or `.../scopes/{sid}/instructions` | Create a tenant-global or scope-specific instruction |
| `instruction_update` | `PUT .../instructions/{iid}` | Update an instruction by id |
| `instruction_delete` | `DELETE .../instructions/{iid}` | Delete an instruction by id |

Routes are shown relative to `/v1.0/api/tenants/{tenantId}` (except `whoami`). Management operations
(endpoint and instruction writes) require tenant administration; the REST server enforces this.
Deliberately **not** exposed over MCP (dashboard/REST-only): tenant, user, and credential management,
server settings, session/token login, and the raw request-history / operation-event feeds.

## Recommended Agent Workflow

Isis is memory, not a filesystem. Read before you write, and prefer summaries before full
bodies to conserve tokens.

1. Call `whoami` to learn your `tenantId`.
2. Call `instructions` with that `tenantId` to read the tenant's standing guidance, then
   `scope_enumerate` to find the scope you want (a project, a book, or a shared "global"
   scope). If no scope fits your project, create one with `scope_create` (use
   `endpoint_enumerate` to pick an embedding endpoint for a semantic RecallDb scope, or to
   confirm one exists).
3. Call `guide` for the selected scope. This is the single most important call: it
   returns the scope's categories, their usage instructions (when and how to write each
   kind of memory), and the store's search capabilities.
4. Use `memory_search` to recall existing memory before doing work. Prefer `Hybrid`
   or `Semantic` mode on a RecallDB-backed scope; `Keyword` always works.
5. Use `memory_enumerate` to browse summaries by category when you want a list rather
   than a query, then `memory_read` to pull the full body of a specific memory.
6. When you learn something durable, write it with `memory_upsert`. Choose a stable
   `slug` so that re-writing the same fact updates it in place instead of duplicating.
7. Create a category with `category_create` only when no existing category fits and
   the guide's instructions do not already cover the content.
8. Use `memory_delete` to remove a memory that is wrong or obsolete.

## Common Arguments

Most tools require `tenantId` and `scopeId`. Obtain `tenantId` from `whoami` and
`scopeId` from `scope_enumerate`. These are validated against the caller's credential
by the REST layer; a caller cannot act on a tenant its credential does not authorize.

## Tool Reference

### `whoami`

Resolve the tenant and principal the caller's credential maps to. Call this first to
discover your `tenantId`.

Proxies `GET /v1.0/api/whoami`.

#### Input

No arguments.

#### Example Request

```json
{}
```

#### Response

```json
{
  "tool": "whoami",
  "success": true,
  "statusCode": 200,
  "data": {
    "tenantId": "ten_a1b2c3",
    "principalType": "Credential",
    "principalName": "default",
    "credentialId": "crd_9x8y7z"
  }
}
```

#### Guidance

- Cache the returned `tenantId` for the rest of the session.
- Callers authenticate with the credential access key (dev default `isisdefaultkey`),
  presented as `Authorization: Bearer <accessKey>` or in the `x-access-key` header; an optional
  `x-secret-key` (dev default `isisdefaultsecret`) is validated only when present. The caller
  resolves to the tenant credential the access key maps to. If that credential's user is an
  admin (`IsAdmin` / `IsTenantAdmin`), the resolved principal reflects it.

### `scope_enumerate`

List the memory scopes in a tenant.

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier from `whoami` |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3"
}
```

#### Response

```json
{
  "tool": "scope_enumerate",
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "scopeId": "scp_repo",
      "name": "agent-memory-repo",
      "storeProvider": "RecallDb"
    },
    {
      "scopeId": "scp_global",
      "name": "global",
      "storeProvider": "RecallDb"
    }
  ]
}
```

#### Guidance

- A scope is a named memory space. Select one by `scopeId` before any category or memory call.
- `storeProvider` tells you whether semantic search is available (`RecallDb`) or whether the
  scope is keyword-only (`Verbex`, `Filesystem`).

### `scope_create`

Create a memory scope for a project when none exists. Use this once, at the start of a project,
after `scope_enumerate` shows no suitable scope.

Proxies `POST /v1.0/api/tenants/{tenantId}/scopes`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier from `whoami` |
| `name` | string | Yes | n/a | Scope name (for example the project name) |
| `description` | string | No | null | What the scope holds |
| `storeProvider` | string | No | server default | Backing store: `RecallDb`, `Verbex`, or `Filesystem` |
| `embeddingEndpointId` | string | No | null | Embedding endpoint id for semantic scopes |
| `dimensionality` | integer | No | null | Embedding vector dimension |
| `filesystemLayout` | string | No | null | Layout for a `Filesystem` scope: `SingleFile`, `Hierarchy`, or `OkfBundle` |
| `targetPath` | string | No | null | Root path for a `Filesystem` scope |
| `chunkingMode` | string | No | `OnOverflow` | When to chunk oversized memory bodies for embedding: `OnOverflow`, `Always`, or `Off` |
| `chunkStrategy` | string | No | `FixedTokenCount` | Chunk splitting strategy (e.g. `FixedTokenCount`, `SentenceBased`, `ParagraphBased`, `Recursive`) |
| `chunkMaxTokens` | integer | No | 0 | Per-chunk token budget (0 = use the embedding model's resolved budget) |
| `chunkOverlapTokens` | integer | No | 64 | Token overlap between adjacent chunks |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "name": "agent-memory-repo",
  "description": "Memory for the AgentMemory project.",
  "storeProvider": "RecallDb"
}
```

#### Response

```json
{
  "tool": "scope_create",
  "success": true,
  "statusCode": 201,
  "data": {
    "scopeId": "scp_repo",
    "name": "agent-memory-repo",
    "storeProvider": "RecallDb"
  }
}
```

#### Guidance

- Enumerate first; create a scope only when none fits. One scope per project is the norm.
- For semantic search, provide an `embeddingEndpointId` and `dimensionality` that match the
  configured embedding endpoint (list them with `endpoint_enumerate`). If you omit them, the
  default RecallDb store auto-selects the tenant's embedding endpoint and its dimensionality.

### `endpoint_enumerate`

List a tenant's configured model endpoints (embedding and inference). Use it to choose an
`embeddingEndpointId` for a semantic (`RecallDb`) scope, or to confirm whether any embedding
endpoint exists at all.

Proxies `GET /v1.0/api/tenants/{tenantId}/endpoints` (optional `kind` filter).

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier from `whoami` |
| `kind` | string | No | all | Filter by endpoint kind: `Embedding` or `Inference` |

#### Guidance

- If no embedding endpoint is configured, prefer a `Filesystem` or `Verbex` (keyword-only) scope.

### `instructions`

Get the tenant's standing instructions — tenant-wide guidance the operator wants every agent to
follow. Returned in ascending `position` order.

Proxies `GET /v1.0/api/tenants/{tenantId}/instructions`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier from `whoami` |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3"
}
```

#### Response

```json
{
  "tool": "instructions",
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "id": "ins_1",
      "name": "House style",
      "content": "Prefer terse commit messages that reference an issue id.",
      "position": 0,
      "active": true
    }
  ]
}
```

#### Guidance

- Read the tenant's instructions early; they are always-on guidance to honor without being asked.
- Instructions are tenant-wide and apply across every scope in the tenant.

### `guide`

Get the operating guide for a scope: its categories, their usage instructions, and store
capabilities. **Call this before writing memory.**

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/guide`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo"
}
```

#### Response

```json
{
  "tool": "guide",
  "success": true,
  "statusCode": 200,
  "data": {
    "scopeId": "scp_repo",
    "categories": [
      {
        "categoryId": "cat_layout",
        "name": "layout",
        "description": "Where things live in the repository.",
        "instructions": "Write one memory per subsystem. Use the subsystem path as the slug."
      }
    ],
    "capabilities": {
      "supportsKeyword": true,
      "supportsSemantic": true,
      "supportsHybrid": true,
      "requiresEmbedding": true,
      "description": "RecallDb store: semantic + keyword + hybrid search."
    }
  }
}
```

#### Guidance

- Treat category `instructions` as the contract for what and how to write.
- `capabilities` tells you which search modes the scope supports (`Semantic` / `Hybrid` require a RecallDb store).
- Tenant-wide standing guidance is separate — fetch it with the `instructions` tool and honor it without being asked.

### `category_enumerate`

List categories in a scope, including their usage instructions.

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo"
}
```

#### Response

```json
{
  "tool": "category_enumerate",
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "categoryId": "cat_layout",
      "name": "layout",
      "description": "Where things live in the repository.",
      "instructions": "Write one memory per subsystem."
    }
  ]
}
```

#### Guidance

- Prefer an existing category over creating a new one.
- `guide` returns the same category information plus the scope's store capabilities; use it for onboarding.

### `category_create`

Create a category in a scope.

Proxies `POST /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `name` | string | Yes | n/a | Category name (unique within the scope; accepted by `memory_search` as a filter) |
| `description` | string | No | null | What the category holds |
| `instructions` | string | No | null | When and how to write memories in this category |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "name": "build-commands",
  "description": "How to build, test, and run the project.",
  "instructions": "One memory per command group. Slug by tool, e.g. build, test, lint."
}
```

#### Response

```json
{
  "tool": "category_create",
  "success": true,
  "statusCode": 201,
  "data": {
    "categoryId": "cat_build",
    "name": "build-commands"
  }
}
```

#### Guidance

- Provide `instructions` so future agents know when and how to write into this category.
- Create categories sparingly; too many fragments dilutes recall.

### `memory_enumerate`

List memory summaries in a scope. Summaries are token-cheap and do not include the full body.

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories` with optional
`category` and `maxResults` query parameters.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `category` | string | No | null | Optional `categoryId` filter |
| `maxResults` | integer | No | server default | Maximum summaries to return |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "category": "cat_layout",
  "maxResults": 50
}
```

#### Response

```json
{
  "tool": "memory_enumerate",
  "success": true,
  "statusCode": 200,
  "data": [
    {
      "id": "mem_1",
      "slug": "filesystem-layout",
      "title": "Where things live in the repo",
      "summary": "src/ holds the server and MCP projects; docs/ holds plans.",
      "categoryId": "cat_layout"
    }
  ]
}
```

#### Guidance

- Enumerate summaries first; call `memory_read` only for the memories you actually need.
- Filter by `category` to keep responses small.

### `memory_read`

Read a single memory by id, returning the full body.

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/{memoryId}`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `memoryId` | string | Yes | n/a | Memory identifier |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "memoryId": "mem_1"
}
```

#### Response

```json
{
  "tool": "memory_read",
  "success": true,
  "statusCode": 200,
  "data": {
    "id": "mem_1",
    "slug": "filesystem-layout",
    "title": "Where things live in the repo",
    "summary": "src/ holds the server and MCP projects; docs/ holds plans.",
    "body": "src/ holds Isis.Core, Isis.Server, and Isis.McpServer. docs/ holds the plan.",
    "categoryId": "cat_layout",
    "tags": ["layout"],
    "links": ["build-commands"]
  }
}
```

#### Guidance

- This is the only tool that returns full memory bodies; use it deliberately.

### `memory_upsert`

Create or update a memory. **Idempotent on `(scope, category, slug)`** — re-writing the
same slug updates the memory in place rather than duplicating it.

Proxies `POST /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `categoryId` | string | Yes | n/a | Target category |
| `slug` | string | Yes | n/a | Stable, link-addressable slug; re-writing updates in place |
| `body` | string | Yes | n/a | The memory content |
| `title` | string | No | null | Human-readable title |
| `summary` | string | No | null | One-line recall hook returned in list/search |
| `type` | string | No | null | One of `User`, `Feedback`, `Project`, `Reference` |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "categoryId": "cat_layout",
  "slug": "filesystem-layout",
  "title": "Where things live in the repo",
  "summary": "src/ holds the server and MCP projects; docs/ holds plans.",
  "body": "src/ holds Isis.Core, Isis.Server, and Isis.McpServer. docs/ holds the plan.",
  "type": "Project"
}
```

#### Response

```json
{
  "tool": "memory_upsert",
  "success": true,
  "statusCode": 200,
  "data": {
    "id": "mem_1",
    "slug": "filesystem-layout",
    "version": 2
  }
}
```

#### Guidance

- Choose a stable, descriptive slug so repeated writes update one memory.
- Provide a crisp `summary`; it is the recall hook shown in enumerate and search results.
- Do not store secrets, credentials, tokens, or raw sensitive data as memory content.

### `memory_search`

Search a scope's memory. Returns ranked results.

Proxies `POST /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/search`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `queryText` | string | Yes | n/a | The search query |
| `mode` | string | No | server default | `Keyword`, `Semantic`, or `Hybrid` |
| `topK` | integer | No | server default | Maximum results to return |
| `categoryName` | string | No | null | Optional category filter: name or `cat_` id (sent as `categoryFilter`). An unknown category returns 400. |
| `minScore` | number | No | null | Drop hits scoring below this. Hybrid scores are fused and normalized to 0..1 |
| `recencyWeight` | number | No | 0.1 | Hybrid only: weight (0..1) of a signal favoring recently written memories; 0 disables |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "queryText": "how do I run the tests",
  "mode": "Hybrid",
  "topK": 5,
  "categoryName": "build-commands"
}
```

#### Response

```json
{
  "tool": "memory_search",
  "success": true,
  "statusCode": 200,
  "data": {
    "hits": [
      {
        "storeKey": "mem_7",
        "slug": "run-tests",
        "title": "Build and run the test suite",
        "snippet": "Build: dotnet build src/Isis.sln -c Release. Test: dotnet run --project src/Test.Automated…",
        "score": 0.83,
        "vectorScore": 0.61,
        "textScore": 0.09,
        "vectorRank": 1,
        "textRank": 2
      }
    ],
    "effectiveMode": "Hybrid"
  }
}
```

#### Guidance

- `Semantic` and `Hybrid` modes require a RecallDB-backed scope; `Keyword` works on any store.
- Search before writing to avoid creating a duplicate memory under a new slug.

### `memory_delete`

Delete a memory by id.

Proxies `DELETE /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/{memoryId}`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `memoryId` | string | Yes | n/a | Memory identifier |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "memoryId": "mem_1"
}
```

#### Response

```json
{
  "tool": "memory_delete",
  "success": true,
  "statusCode": 204,
  "data": null
}
```

#### Guidance

- Delete memories that are proven wrong or obsolete rather than leaving stale guidance.

## Error Behavior

A request with no access key is rejected at the transport layer before any tool runs (present
the access key as `Authorization: Bearer <accessKey>` or `x-access-key`):

```text
HTTP 401
```

A missing required argument raises an error from the tool:

```text
Argument 'tenantId' is required.
```

Downstream REST failures are returned in the envelope with `success: false` and the
upstream `statusCode`:

```json
{
  "tool": "memory_read",
  "success": false,
  "statusCode": 404,
  "data": { "error": "Memory 'mem_missing' not found." }
}
```

```json
{
  "tool": "scope_enumerate",
  "success": false,
  "statusCode": 403,
  "data": { "error": "Credential is not authorized for tenant 'ten_other'." }
}
```

## Related Documents

- [CONNECTING_AGENTS.md](CONNECTING_AGENTS.md) — connect Claude Code, Cursor, and generic
  MCP clients to Isis, including the `isis mcp install` helper.
- [ISIS_PLAN.md](ISIS_PLAN.md) — full product plan, including the REST API surface.
</content>
</invoke>
