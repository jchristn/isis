# Isis Documentation

Isis is an agent-memory platform: durable, structured, queryable memory that survives
across sessions, harnesses, and projects. Agents reach it over **MCP**; operators manage
it over **REST** and a dashboard.

## Documents

| Document | What it covers |
|----------|----------------|
| [CONNECTING_AGENTS.md](CONNECTING_AGENTS.md) | Connect an agent to Isis over MCP: endpoint, auth headers, ready-to-paste config for Claude Code / Cursor / generic MCP clients, the `isis mcp install` helper, a first-calls walkthrough, and troubleshooting. **Start here to connect an agent.** |
| [MCP_API.md](MCP_API.md) | Full reference for the ten `isis_*` MCP tools: purpose, arguments, example JSON-RPC requests and responses, the transport/endpoint, the response envelope, and the auth model. |
| [ISIS_PLAN.md](ISIS_PLAN.md) | The product plan: architecture, domain model, storage/search providers, REST API surface, MCP surface, dashboard, deployment, and roadmap. |

## MCP at a Glance

- **Endpoint:** `http://localhost:8720/mcp` (streamable HTTP + SSE)
- **Auth:** the credential **access key**, sent as `Authorization: Bearer isisdefaultkey` or `x-access-key: isisdefaultkey`; the access key authenticates on its own (a capability token). Every agent sends the access key alone — Mux as a bearer token, Claude Code / Codex / Cursor / Gemini in the `x-access-key` header; none send a secret. The server still accepts an optional `x-secret-key` header and validates it only when present, but no installer sends one.
- **Tools:** `isis_whoami`, `isis_scope_enumerate`, `isis_guide`, `isis_category_enumerate`,
  `isis_category_create`, `isis_memory_enumerate`, `isis_memory_read`, `isis_memory_upsert`,
  `isis_memory_search`, `isis_memory_delete`
- **Typical flow:** `isis_whoami` -> `isis_scope_enumerate` -> `isis_guide` ->
  `isis_memory_search` / `isis_memory_upsert`

Connect Claude Code in one step with `isis mcp install`. See
[CONNECTING_AGENTS.md](CONNECTING_AGENTS.md) for every client.
</content>
