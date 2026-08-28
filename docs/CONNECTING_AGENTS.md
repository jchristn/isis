# Connecting Agents to Isis over MCP

This guide connects an AI agent to Isis's memory platform through MCP. It covers the
endpoint and headers, ready-to-paste configuration for common clients, a first-calls
walkthrough, and troubleshooting.

For the full tool contract, response schemas, and per-tool guidance, see
[MCP_API.md](MCP_API.md).

## Endpoint and Headers

| Property | Value |
|----------|-------|
| Endpoint | `http://localhost:8720/mcp` |
| Transport | Streamable HTTP + SSE |
| Auth (access key) | `Authorization: Bearer isisdefaultkey` **or** `x-access-key: isisdefaultkey` |

Isis authenticates a caller by its credential **access key**, which on its own identifies
the tenant credential and scopes the connection to that credential's tenant. Present the
access key either as a bearer token (`Authorization: Bearer <accessKey>`) or in the
`x-access-key` header. Every agent authenticates with the access key alone: **Mux** sends it
as a bearer token (see the Mux section), and **Claude Code, Codex, Cursor, and Gemini** send it
in the `x-access-key` header. No agent sends a secret key — it never leaves the client. The
server still *optionally* accepts an `x-secret-key` header and validates it when present, but
none of the installers send one. Because the access key alone authenticates, treat it as a
**capability token** and prefer a least-privilege credential. The values above are the shipped
defaults; change them before exposing Isis outside a trusted local environment.

The host and port are configured in `isis.mcp.json` and can be overridden with the
`ISIS_MCP_HOSTNAME` and `ISIS_MCP_PORT` environment variables. If you change the port,
substitute it everywhere `8720` appears below.

## Quick Start: `isis mcp install`

The fastest way to connect Claude Code is the built-in installer, which writes the Isis
MCP entry into your Claude Code user configuration for you:

```bash
isis mcp install
```

This patches `~/.claude.json` with an `isis` MCP server pointing at
`http://127.0.0.1:8720/mcp`, including the `x-access-key` header. It reads the port and host
from `isis.mcp.json` and the `ISIS_MCP_*` environment variables, and accepts optional
`--access-key`, `--port`, and `--host` flags. After it runs, restart Claude Code to pick up the
change. See [Installer Reference](#installer-reference) for details.

To connect manually, or to connect a different client, use the snippets below.

## Claude Code

### Option A: `claude mcp add` (CLI)

Add Isis as an HTTP MCP server with the access-key header inline:

```bash
claude mcp add --transport http isis http://localhost:8720/mcp --header "x-access-key: isisdefaultkey"
```

The access key is the only header required; it identifies the credential and authenticates on
its own. The secret key is never sent.

Restart Claude Code (or reload the MCP servers) after adding.

### Option B: Project `.mcp.json`

Commit a `.mcp.json` file at the root of your project so every agent working in that repo
shares the same Isis connection:

```json
{
  "mcpServers": {
    "isis": {
      "type": "http",
      "url": "http://localhost:8720/mcp",
      "headers": {
        "x-access-key": "isisdefaultkey"
      }
    }
  }
}
```

Only the access-key header is required; the secret key is never sent. Because the access key is
a capability token, do not commit production credentials into a shared repository; prefer a
credential with least privilege, or keep the file untracked.

## Cursor

Add an `mcpServers` block to `~/.cursor/mcp.json` (global) or `.cursor/mcp.json` (per
project):

```json
{
  "mcpServers": {
    "isis": {
      "url": "http://localhost:8720/mcp",
      "headers": {
        "x-access-key": "isisdefaultkey"
      }
    }
  }
}
```

Only the access-key header is required; the secret key is never sent. Restart Cursor after
saving so it reconnects to the MCP server.

## Mux

Mux can send only **one** auth header, so it authenticates with the credential **access key
carried as a bearer token** — it does **not** send `x-access-key` / `x-secret-key`, and the
secret key never leaves your machine. Add an `isis` entry to Mux's `mcp-servers.json` (its
`servers` array) with a `bearer` auth block whose token is your access key:

```json
{
  "servers": [
    {
      "name": "isis",
      "transport": "http",
      "url": "http://localhost:8720",
      "mcpPath": "/mcp",
      "auth": { "type": "bearer", "bearerToken": "isisdefaultkey" }
    }
  ]
}
```

Point Mux at this file with `--mcp-config`, or use the `scripts/*/install-mux.*` helper.
Because the access key alone authenticates, it is a **capability token** — scope it
least-privilege. See [INSTRUCTIONS_FOR_MUX.md](INSTRUCTIONS_FOR_MUX.md) for the full Mux
walkthrough.

## Generic MCP Client

Any MCP client that speaks streamable HTTP can connect. Point it at the endpoint and set the
access-key header (or send the access key as a bearer token):

```json
{
  "type": "http",
  "url": "http://localhost:8720/mcp",
  "headers": {
    "x-access-key": "isisdefaultkey"
  }
}
```

The MCP `initialize` handshake succeeds over streamable HTTP + SSE, after which
`tools/list` returns the ten `isis_*` tools. Clients that only support the classic
JSON-RPC path can use `http://localhost:8720/rpc`, but `/mcp` is preferred.

You can verify the endpoint is reachable and correctly authenticated with a raw MCP
`initialize` call:

```bash
curl -s http://localhost:8720/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "x-access-key: isisdefaultkey" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'
```

This example sends the access key in the `x-access-key` header; a bearer token
(`Authorization: Bearer isisdefaultkey`) authenticates equivalently. A request with no access
key at all returns `401`.

## First Calls Walkthrough

Once connected, drive Isis in this order. Isis is memory, not a filesystem: read before
you write, and prefer summaries before full bodies.

### 1. Discover your tenant

Call `isis_whoami` with no arguments. It returns the `tenantId` your credential maps to.

```json
{}
```

Response `data`:

```json
{ "tenantId": "ten_a1b2c3", "principalType": "Credential", "principalId": "crd_9x8y7z" }
```

### 2. Find a scope and read its guide

List scopes, then read the guide for the one you want. The guide returns the scope's
categories, their usage instructions, and active policies.

`isis_scope_enumerate`:

```json
{ "tenantId": "ten_a1b2c3" }
```

`isis_guide`:

```json
{ "tenantId": "ten_a1b2c3", "scopeId": "scp_repo" }
```

### 3. Write a memory

Use `isis_memory_upsert` with a stable slug so future writes update the same memory
instead of duplicating it.

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

### 4. Recall it

Search the scope with `isis_memory_search`. Use `Hybrid` on a RecallDB-backed scope;
`Keyword` works everywhere.

```json
{
  "tenantId": "ten_a1b2c3",
  "scopeId": "scp_repo",
  "queryText": "where does the MCP server live",
  "mode": "Hybrid",
  "topK": 5
}
```

The result carries summaries and ids; call `isis_memory_read` with an id to pull the full
body when you need it.

## Troubleshooting

### `401` on connect

The request reached Isis but carried no access key. Every request needs the credential access
key — sent either as `Authorization: Bearer <accessKey>` (Mux) or in the `x-access-key` header
(Claude Code, Codex, Cursor, Gemini). No agent sends a secret key. Some clients require the
header under a `headers` object (see the snippets above) rather than as a URL parameter.

### `403` on a tenant call

Your credential is not authorized for the `tenantId` you passed. Call `isis_whoami` and use
the `tenantId` it returns. A credential can only operate on its own tenant unless its user
holds the `IsAdmin` flag for cross-tenant work.

### The client connects but lists no tools

Confirm the URL ends in `/mcp` and the transport is set to HTTP (streamable HTTP + SSE).
The classic JSON-RPC path is `/rpc`; the SSE events path is `/events`. If your client
pins an older MCP protocol version, update it — the Isis `initialize` handshake targets
current streamable HTTP.

### Connection refused

The Isis MCP server is not listening on the expected host/port. Confirm it is running and
check `isis.mcp.json` (`Hostname`, `Port`) plus the `ISIS_MCP_HOSTNAME` / `ISIS_MCP_PORT`
overrides. The default is `127.0.0.1:8720`. Note that `127.0.0.1` is preferred over
`localhost` on Windows to avoid the IPv6 (`::1`) resolution stall.

### Tools return `success: false` with `statusCode` 502/503

The MCP server is up but cannot reach the Isis REST API it proxies. Confirm the REST API
is running and that `RestHostname` / `RestPort` in `isis.mcp.json` (or `ISIS_MCP_REST_HOSTNAME`
/ `ISIS_MCP_REST_PORT`) point at it. The default REST target is `127.0.0.1:8700`.

### Changes to `.mcp.json` or `~/.claude.json` are not picked up

Restart the agent or client after editing MCP configuration. Running `isis mcp install`
again is safe and idempotent; it updates the existing `isis` entry in place.

## Installer Reference

`isis mcp install` writes the Claude Code user configuration entry so you do not have to
edit JSON by hand. It is safe to run repeatedly: it updates the existing `isis` entry in
place and preserves every other MCP server and setting in the file. See
[MCP_API.md](MCP_API.md) for the tool contract the connected agent will use.

## Related Documents

- [MCP_API.md](MCP_API.md) — the full MCP tool reference.
- [ISIS_PLAN.md](ISIS_PLAN.md) — the product plan and REST API surface.
</content>
