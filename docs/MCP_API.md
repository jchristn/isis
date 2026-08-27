# Isis MCP API

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

Every MCP request must present a credential access key **and** its secret key, one in each
of two request headers. The MCP server rejects requests that are missing either header with
HTTP `401` and the message `Provide x-access-key and x-secret-key headers.`

| Header | Value | Default value | Use |
|--------|-------|---------------|-----|
| `x-access-key` | Credential access key | `isisdefaultkey` | Identifies the tenant credential |
| `x-secret-key` | Credential secret key | `isisdefaultsecret` | Proves the caller holds the credential |

Send both headers on every request. Together they identify a tenant credential; the access
key alone is no longer sufficient — the secret is now required. Both values are forwarded
verbatim to the Isis REST API, which enforces tenant isolation. Tenant identity is never
trusted from a tool argument alone; the REST layer validates that the caller's credential is
authorized for the `tenantId` it operates on. Administrative power, when a credential's user
has it, comes from the user record's `IsAdmin` (system-wide) or `IsTenantAdmin` (tenant-wide)
flags — there is no separate admin key.

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
  "tool": "isis_whoami",
  "success": true,
  "statusCode": 200,
  "data": {
    "tenantId": "ten_a1b2c3",
    "principalType": "Credential",
    "principalId": "crd_9x8y7z"
  }
}
```

When the REST call fails, `success` is `false`, `statusCode` carries the upstream code
(for example `401`, `403`, `404`), and `data` contains the REST error body.

## Tool Inventory

Isis exposes ten MCP tools.

| Tool | Purpose |
|------|---------|
| `isis_whoami` | Resolve the tenant and principal the caller's credential maps to |
| `isis_scope_enumerate` | List the memory scopes in a tenant |
| `isis_guide` | Get the operating guide for a scope: categories, usage instructions, capabilities |
| `isis_category_enumerate` | List categories in a scope, including usage instructions |
| `isis_category_create` | Create a category in a scope |
| `isis_memory_enumerate` | List memory summaries in a scope (token-cheap) |
| `isis_memory_read` | Read a single memory by id, returning the full body |
| `isis_memory_upsert` | Create or update a memory; idempotent on `(scope, category, slug)` |
| `isis_memory_search` | Search a scope's memory (keyword, semantic, or hybrid) |
| `isis_memory_delete` | Delete a memory by id |

## Recommended Agent Workflow

Isis is memory, not a filesystem. Read before you write, and prefer summaries before full
bodies to conserve tokens.

1. Call `isis_whoami` to learn your `tenantId`.
2. Call `isis_scope_enumerate` with that `tenantId` to find the scope you want (a project,
   a book, or a shared "global" scope).
3. Call `isis_guide` for the selected scope. This is the single most important call: it
   returns the scope's categories, their usage instructions (when and how to write each
   kind of memory), and the store's search capabilities.
4. Use `isis_memory_search` to recall existing memory before doing work. Prefer `Hybrid`
   or `Semantic` mode on a RecallDB-backed scope; `Keyword` always works.
5. Use `isis_memory_enumerate` to browse summaries by category when you want a list rather
   than a query, then `isis_memory_read` to pull the full body of a specific memory.
6. When you learn something durable, write it with `isis_memory_upsert`. Choose a stable
   `slug` so that re-writing the same fact updates it in place instead of duplicating.
7. Create a category with `isis_category_create` only when no existing category fits and
   the guide's instructions do not already cover the content.
8. Use `isis_memory_delete` to remove a memory that is wrong or obsolete.

## Common Arguments

Most tools require `tenantId` and `scopeId`. Obtain `tenantId` from `isis_whoami` and
`scopeId` from `isis_scope_enumerate`. These are validated against the caller's credential
by the REST layer; a caller cannot act on a tenant its credential does not authorize.

## Tool Reference

### `isis_whoami`

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
  "tool": "isis_whoami",
  "success": true,
  "statusCode": 200,
  "data": {
    "tenantId": "ten_a1b2c3",
    "principalType": "Credential",
    "principalId": "crd_9x8y7z"
  }
}
```

#### Guidance

- Cache the returned `tenantId` for the rest of the session.
- The caller resolves to the tenant credential its `x-access-key`/`x-secret-key` pair maps
  to. If that credential's user is an admin (`IsAdmin` / `IsTenantAdmin`), the resolved
  principal reflects it.

### `isis_scope_enumerate`

List the memory scopes in a tenant.

Proxies `GET /v1.0/api/tenants/{tenantId}/scopes`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier from `isis_whoami` |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3"
}
```

#### Response

```json
{
  "tool": "isis_scope_enumerate",
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

### `isis_guide`

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
  "tool": "isis_guide",
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
    "policies": [
      {
        "policyId": "pol_style",
        "name": "commit-style",
        "text": "Commit messages use imperative mood and reference an issue id."
      }
    ]
  }
}
```

#### Guidance

- Treat category `instructions` as the contract for what and how to write.
- Policies are always-on cross-cutting guidance; honor them without being asked.

### `isis_category_enumerate`

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
  "tool": "isis_category_enumerate",
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
- `isis_guide` returns the same category information plus policies; use it for onboarding.

### `isis_category_create`

Create a category in a scope.

Proxies `POST /v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories`.

#### Input

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `tenantId` | string | Yes | n/a | Tenant identifier |
| `scopeId` | string | Yes | n/a | Scope identifier |
| `name` | string | Yes | n/a | Category name (also used as the RecallDB label) |
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
  "tool": "isis_category_create",
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

### `isis_memory_enumerate`

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
  "tool": "isis_memory_enumerate",
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

- Enumerate summaries first; call `isis_memory_read` only for the memories you actually need.
- Filter by `category` to keep responses small.

### `isis_memory_read`

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
  "tool": "isis_memory_read",
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

### `isis_memory_upsert`

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
  "tool": "isis_memory_upsert",
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

### `isis_memory_search`

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
| `category` | string | No | null | Optional category name filter (sent as `categoryFilter`) |

#### Example Request

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "queryText": "how do I run the tests",
  "mode": "Hybrid",
  "topK": 5,
  "category": "build-commands"
}
```

#### Response

```json
{
  "tool": "isis_memory_search",
  "success": true,
  "statusCode": 200,
  "data": {
    "results": [
      {
        "id": "mem_7",
        "slug": "run-tests",
        "summary": "Run test.bat, or dotnet test on the Isis solution.",
        "score": 0.83
      }
    ]
  }
}
```

#### Guidance

- `Semantic` and `Hybrid` modes require a RecallDB-backed scope; `Keyword` works on any store.
- Search before writing to avoid creating a duplicate memory under a new slug.

### `isis_memory_delete`

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
  "tool": "isis_memory_delete",
  "success": true,
  "statusCode": 204,
  "data": null
}
```

#### Guidance

- Delete memories that are proven wrong or obsolete rather than leaving stale guidance.

## Error Behavior

A missing credential is rejected at the transport layer before any tool runs:

```text
HTTP 401  Provide x-access-key and x-secret-key headers.
```

A missing required argument raises an error from the tool:

```text
Argument 'tenantId' is required.
```

Downstream REST failures are returned in the envelope with `success: false` and the
upstream `statusCode`:

```json
{
  "tool": "isis_memory_read",
  "success": false,
  "statusCode": 404,
  "data": { "error": "Memory 'mem_missing' not found." }
}
```

```json
{
  "tool": "isis_scope_enumerate",
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
