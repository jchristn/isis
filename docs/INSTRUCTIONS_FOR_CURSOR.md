> **This document is meant to be pasted into Cursor's project rules or system prompt.** It gives Cursor everything it needs to connect to and use the Isis agent-memory platform. Copy the contents below into your Cursor configuration.

---

## Connecting Cursor to Isis over MCP

Isis serves the modern **MCP Streamable HTTP + SSE** transport at `http://127.0.0.1:8720/mcp`. Every request must carry two auth headers: `x-access-key` (a credential access key, default `isisdefaultkey`) and `x-secret-key` (its secret key, default `isisdefaultsecret`). Together they identify a tenant credential and scope the connection to its tenant. Change the defaults before exposing Isis outside a trusted local environment.

### Config file -- `~/.cursor/mcp.json`

Cursor reads its MCP servers from `~/.cursor/mcp.json` (global) or `.cursor/mcp.json` (per project). Add an `isis` entry under `mcpServers` with the endpoint and both auth headers:

```json
{
  "mcpServers": {
    "isis": {
      "url": "http://127.0.0.1:8720/mcp",
      "headers": {
        "x-access-key": "isisdefaultkey",
        "x-secret-key": "isisdefaultsecret"
      }
    }
  }
}
```

Both headers are required. Restart Cursor after saving so it reconnects; open **Settings -> MCP** to confirm the `isis` server is green and the ten `isis_*` tools are listed.

---

# Isis Memory Instructions

You have access to the Isis agent-memory platform via MCP tools. Isis is **not** an orchestrator -- it is durable, shared **memory**. Use it to recall what you (or another agent) learned before, and to record durable facts so the next session does not start from zero. Isis is memory, not a filesystem: read before you write, and prefer summaries before full bodies to conserve tokens.

## Concepts

| Term | What it is | ID prefix |
|------|-----------|-----------|
| **Tenant** | An isolated memory account; everything you read and write lives under one tenant | `ten_` |
| **Scope** | A memory space -- a project, a book, or a shared "global" space -- backed by a store (RecallDb, Verbex, or Filesystem) | `scp_` |
| **Category** | A labeled bucket within a scope that carries usage instructions for what to write and how | `cat_` |
| **Memory** | One atomic note: `slug`, `title`, `body`, `summary`, `tags` | `mem_` |
| **Credential** | The access key your connection authenticates with; maps to a tenant | `crd_` |

**Chat-with-Memory** is retrieval-augmented reasoning over a whole scope: rather than reading memories one by one, a scope's memory can answer a natural-language question by searching and synthesizing across its notes. Under the hood it is the same `isis_memory_search` recall you drive directly with the tools below.

The store behind a scope determines search power: `RecallDb` supports `Semantic` and `Hybrid` search; `Verbex` and `Filesystem` are `Keyword`-only.

## Core Workflow

Every memory session follows this pattern: **Discover -> Recall -> Record -> Curate**

### 1. Discover

Learn who you are and what memory exists before doing anything else:

```
isis_whoami()                                   -> your tenantId (cache it for the session)
isis_scope_enumerate({ tenantId })              -> the scopes available in your tenant
isis_guide({ tenantId, scopeId })               -> the scope's categories, their usage instructions, and policies
```

`isis_guide` is the single most important call. It returns each category's **instructions** -- the contract for when and how to write that kind of memory -- plus always-on **policies** you should honor without being asked. Read it before writing anything.

### 2. Recall

Before you do work, check whether the answer is already remembered:

```
isis_memory_search({ tenantId, scopeId, queryText, mode: "Hybrid", topK: 5 })
isis_memory_enumerate({ tenantId, scopeId, category })   -> token-cheap summaries by category
isis_memory_read({ tenantId, scopeId, memoryId })        -> the full body of one memory
```

Search first (`Hybrid` or `Semantic` on a RecallDb scope; `Keyword` works anywhere). Enumerate when you want a list rather than a query. Only call `isis_memory_read` for the specific memories you actually need -- it is the only tool that returns full bodies.

### 3. Record

When you learn something durable, write it:

```
isis_memory_upsert({ tenantId, scopeId, categoryId, slug, title, summary, body, type })
```

`isis_memory_upsert` is **idempotent on `(scope, category, slug)`** -- re-writing the same slug updates the memory in place instead of duplicating it. Choose a stable, descriptive slug. Provide a crisp one-line `summary`; it is the recall hook shown in enumerate and search results.

Create a category only when no existing one fits:

```
isis_category_create({ tenantId, scopeId, name, description, instructions })
```

Always supply `instructions` so future agents know when and how to write into it. Create categories sparingly -- too many fragments dilutes recall.

### 4. Curate

Keep memory trustworthy. Delete what is proven wrong or obsolete rather than leaving stale guidance behind:

```
isis_memory_delete({ tenantId, scopeId, memoryId })
```

## When to Write a Memory

Write a memory when you learn something a future session would otherwise have to rediscover:

- **Durable facts about the project** -- where a subsystem lives, how to build/test/run, a non-obvious convention, an architectural decision and its rationale.
- **User preferences and feedback** -- how the user wants things done, corrections they made, standing instructions.
- **Reference material** -- API shapes, config keys, external dependencies and their quirks.
- **Outcomes worth remembering** -- what a fix was, why an approach failed, what to try next.

Do **not** write:

- Transient state that will be false next hour (open file, current cursor position).
- Anything the `isis_guide` categories tell you does not belong.
- **Secrets, credentials, tokens, or raw sensitive data.** Never store these as memory content.

Match every write to a category and follow that category's `instructions`. When in doubt, search first -- if a near-duplicate exists, update it by re-using its slug instead of creating a second copy.

## Tool Reference

| Tool | Parameters | Description |
|------|-----------|-------------|
| `isis_whoami` | -- | Resolve the tenant and principal your credential maps to. Call first. |
| `isis_scope_enumerate` | `tenantId` (required) | List the memory scopes in a tenant. |
| `isis_guide` | `tenantId`, `scopeId` (required) | The scope's categories, their usage instructions, and policies. Call before writing. |
| `isis_category_enumerate` | `tenantId`, `scopeId` (required) | List categories in a scope, including usage instructions. |
| `isis_category_create` | `tenantId`, `scopeId`, `name` (required); `description`, `instructions` | Create a category. Supply `instructions`. |
| `isis_memory_enumerate` | `tenantId`, `scopeId` (required); `category`, `maxResults` | List token-cheap memory summaries (no bodies). |
| `isis_memory_read` | `tenantId`, `scopeId`, `memoryId` (required) | Read one memory's full body. |
| `isis_memory_upsert` | `tenantId`, `scopeId`, `categoryId`, `slug`, `body` (required); `title`, `summary`, `type` | Create or update a memory. Idempotent on `(scope, category, slug)`. |
| `isis_memory_search` | `tenantId`, `scopeId`, `queryText` (required); `mode`, `topK`, `category` | Search a scope. `mode` = `Keyword`/`Semantic`/`Hybrid`. |
| `isis_memory_delete` | `tenantId`, `scopeId`, `memoryId` (required) | Delete a memory by id. |

`type` on upsert is one of `User`, `Feedback`, `Project`, `Reference`. `Semantic` and `Hybrid` search require a RecallDb-backed scope; `Keyword` works on any store.

## Decision-Making Guidance

- **Always start with `isis_whoami`** and cache the `tenantId` -- nearly every other tool requires it. `scopeId` comes from `isis_scope_enumerate`.
- **Read the guide before writing.** Category `instructions` and scope `policies` are the contract; honor them.
- **Prefer summaries to bodies.** Enumerate and search return token-cheap summaries; only `isis_memory_read` pulls a full body. Pull bodies deliberately.
- **Search before you write** to avoid creating a duplicate under a new slug. If a memory exists, update it by re-using its slug.
- **Keep slugs stable and descriptive** so repeated writes converge on one memory instead of scattering.
- **Curate as you go.** A wrong memory is worse than a missing one -- delete or overwrite stale guidance.

## Response Envelope

Every tool returns the same envelope; the proxied REST response is under `data`:

```json
{ "tool": "isis_whoami", "success": true, "statusCode": 200, "data": { "tenantId": "ten_a1b2c3", "principalType": "Credential", "principalId": "crd_9x8y7z" } }
```

When a call fails, `success` is `false`, `statusCode` carries the upstream code (e.g. `401`, `403`, `404`), and `data` holds the error body. A missing auth header is rejected before any tool runs with `401 Provide x-access-key and x-secret-key headers.` A `403` on a tenant call means your credential is not authorized for that `tenantId` -- call `isis_whoami` and use the `tenantId` it returns.
