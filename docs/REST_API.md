# Isis REST API

Base URL (local dev): `http://127.0.0.1:8700`
Management API prefix: `/v1.0/api`
OpenAPI document: `GET /openapi.json` (served from the server root)

All request and response bodies are JSON. Enums serialize as strings; property names are camelCase.

---

## Authentication

Isis has exactly **two** authentication mechanisms. There is no admin API key.

### 1. Email / password → session token (interactive users, dashboard)

Login is a three-step flow so a user only needs their email, a tenant choice, and a password:

**Step 1 — discover tenants for an email (anonymous):**

```
POST /v1.0/api/tenants-for-email
Content-Type: application/json

{ "email": "admin@isis.local" }
```

```json
{ "tenants": [ { "id": "ten_default", "name": "Default" } ] }
```

Email is unique only *within* a tenant, so an address may belong to more than one tenant. If exactly one is returned, use it; otherwise let the user pick by **name**.

**Step 2 — exchange credentials for a token (anonymous):**

```
POST /v1.0/api/token
Content-Type: application/json

{ "email": "admin@isis.local", "password": "isisadmin", "tenantId": "ten_default" }
```

```json
{
  "token": "…opaque…",
  "tenantId": "ten_default",
  "userId": "usr_admin",
  "email": "admin@isis.local",
  "isAdmin": true,
  "isTenantAdmin": true,
  "expiresUtc": "2026-08-27T12:00:00Z"
}
```

Invalid email, password, or tenant returns `401` with `{ "error": "Unauthorized", "message": "Invalid credentials." }` (the same message for each, so the endpoint does not reveal which field was wrong).

**Step 3 — call the API with the token:**

```
GET /v1.0/api/whoami
Authorization: Bearer <token>
```

`x-token: <token>` is accepted as an alternate carrier. If both are present they must match.

**Logout (revoke the session):**

```
DELETE /v1.0/api/token
Authorization: Bearer <token>
```

Tokens are revocable server-side and stop working immediately when the session, user, or tenant is disabled. The token is tenant-bound: a token for tenant A cannot act on tenant B.

### 2. Credential access key (automation, MCP, agents)

Send the access key on every request:

```
x-access-key: isisdefaultkey
```

The **access key alone authenticates** — it is the public, transferable material, so treat it as a capability token and scope it least-privilege. The secret key is **not required** on the wire and never has to leave the client; MCP agents (Mux, Claude, Codex, Cursor, Gemini) all connect with the access key only. If a request *does* include an `x-secret-key` header it is validated against the credential (a wrong secret is rejected), so a stricter caller may still send it — but no installer does. A credential resolves to its owning user's tenant and inherits the owner's `IsAdmin` / `IsTenantAdmin` flags. The raw secret key is shown only once, at creation.

### Admin model

Administrative authority comes solely from the user record:

- `IsAdmin` — system-wide; may manage any tenant and bypasses tenant checks.
- `IsTenantAdmin` — full control within the user's own tenant only.

A fresh deployment seeds a default admin user (`admin@isis.local` / `isisadmin`, tenant `ten_default`) with `IsAdmin = true`, and a default credential (`isisdefaultkey` / `isisdefaultsecret`). Override every value via environment before any shared deployment (`ISIS_AUTH_SEED_ADMIN_EMAIL`, `ISIS_AUTH_SEED_ADMIN_PASSWORD`, `ISIS_AUTH_DEFAULT_ACCESS_KEY`, `ISIS_AUTH_DEFAULT_SECRET_KEY`).

---

## Endpoints

### Authentication

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| POST | `/v1.0/api/tenants-for-email` | anonymous | List tenants an email belongs to |
| POST | `/v1.0/api/token` | anonymous | Issue a session token from email/password |
| GET | `/v1.0/api/whoami` | session or credential | Resolve the current principal |
| DELETE | `/v1.0/api/token` | session | Revoke the current session (logout) |

### System

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/v1.0/api/health` | anonymous | Node + database health |
| GET | `/v1.0/api/server/info` | session or credential | Product / version / node |

### Tenants (system administrator only)

| Method | Path | Description |
| --- | --- | --- |
| GET | `/v1.0/api/tenants` | List tenants |
| POST | `/v1.0/api/tenants` | Create a tenant (auto-provisions admin + credential + default instructions) |
| GET | `/v1.0/api/tenants/{tenantId}` | Read a tenant |
| PUT | `/v1.0/api/tenants/{tenantId}` | Update a tenant |
| DELETE | `/v1.0/api/tenants/{tenantId}` | Delete a tenant and cascade to all its children (409 for protected tenants) |

Creating a tenant provisions a full, ready-to-use environment: the tenant, an auto-generated
tenant-admin user, a credential for that user, and a default instruction set. The one-time
`password` and `secretKey` are returned **only in this response** — capture them now.

Create body:

```json
{ "name": "Demo" }
```

Create response (password and secretKey shown once):

```json
{
  "tenant": { "id": "ten_…", "name": "Demo" },
  "admin": { "userId": "usr_…", "email": "admin@ten_….local", "password": "<one-time>" },
  "credential": { "credentialId": "crd_…", "accessKey": "access_…", "secretKey": "secret_…<one-time>" }
}
```

Deleting a tenant is the **"Nuke tenant"** operation: it cascades to ALL of the tenant's users,
credentials, scopes, categories, memories, instructions, endpoints, and sessions — including
external memory-store content. Protected tenants (e.g. `ten_default`) are refused with **409**.

### Users (tenant administrator or system administrator)

Passwords are hashed server-side and never returned.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/v1.0/api/tenants/{tenantId}/users` | List users |
| POST | `/v1.0/api/tenants/{tenantId}/users` | Create a user |
| GET | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Read a user |
| PUT | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Update a user |
| DELETE | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Delete a user (cascades to that user's credentials, sessions, and permissions) |

Create/update body:

```json
{
  "firstName": "Ada",
  "lastName": "Lovelace",
  "email": "ada@example.com",
  "password": "s3cret",
  "isAdmin": false,
  "isTenantAdmin": true,
  "active": true
}
```

`password` is required on create and optional on update (omit to keep the current password).

### Credentials (tenant administrator or system administrator)

The server generates the access key and secret key. The **raw secret is returned once**, in the `secretKey` field of the create response; afterwards only `secretKeyLast4` is exposed.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/v1.0/api/tenants/{tenantId}/credentials` | List credentials |
| POST | `/v1.0/api/tenants/{tenantId}/credentials` | Create a credential |
| GET | `/v1.0/api/tenants/{tenantId}/credentials/{credentialId}` | Read a credential |
| PUT | `/v1.0/api/tenants/{tenantId}/credentials/{credentialId}` | Update a credential |
| DELETE | `/v1.0/api/tenants/{tenantId}/credentials/{credentialId}` | Delete a credential |

Create body:

```json
{ "name": "CI pipeline", "userId": "usr_admin", "active": true }
```

Create response (secret shown once):

```json
{
  "id": "crd_…",
  "accessKey": "access_…",
  "secretKey": "secret_…",
  "secretKeyLast4": "…",
  "name": "CI pipeline",
  "userId": "usr_admin",
  "active": true
}
```

### Memory domain (tenant-scoped)

All require access to the tenant (`tenantId` in the path); the token/credential is confined to its own tenant.

| Method | Path | Description |
| --- | --- | --- |
| GET/POST | `/v1.0/api/tenants/{tenantId}/scopes` | List / create scopes |
| GET/PUT/DELETE | `/v1.0/api/tenants/{tenantId}/scopes/{scopeId}` | Read / update / delete a scope (delete cascades to its categories + memories and drops the store collection/files) |
| GET/POST | `…/scopes/{scopeId}/categories` | List / create categories |
| GET/PUT/DELETE | `…/scopes/{scopeId}/categories/{categoryId}` | Read / update / delete a category (delete cascades to its memories) |
| GET/POST | `…/scopes/{scopeId}/memories` | List / upsert memories |
| GET/DELETE | `…/scopes/{scopeId}/memories/{memoryId}` | Read / delete a memory |
| POST | `…/scopes/{scopeId}/memories/search` | Keyword / semantic / hybrid search |
| POST | `…/scopes/{scopeId}/chat` | Chat with memory |
| GET | `…/scopes/{scopeId}/guide` | Agent onboarding manifest |
| GET/POST | `…/endpoints` | List / create model endpoints |
| GET/PUT/DELETE | `…/endpoints/{endpointId}` | Read / update / delete an endpoint |
| GET | `…/endpoint-health` | Aggregate endpoint health |
| GET/POST/DELETE | `…/collections` | RecallDB collections pass-through |

Instructions are either **tenant-global** (managed at `…/instructions`) or **scope-specific**
(managed at `…/scopes/{scopeId}/instructions`). A scope-specific instruction carries a `mergeMode`
of `Append` (add a new instruction), `Replace` (override a same-named global's content in place), or
`Hide` (suppress a same-named global). `GET …/scopes/{scopeId}/effective-instructions` returns the
merged, source-annotated result an agent working in that scope should see. Over MCP, the
`isis_instructions` tool takes an optional `scopeId` and returns that scope's effective set.

A model endpoint is addressed by a full **`baseUrl`** (e.g. `http://host:11434` or
`http://conductor.example.com:8900/v1.0/api/all-minilm-latest`) onto which the API-format path is
appended — there is no separate host/port/ssl. Outbound authentication is configured with
`authType` — one of `None`, `BearerToken`, `ApiKeyHeader` (with `authHeaderName` + `authSecret`),
`QueryParam` (with `authQueryParam` + `authSecret`), `BasicAuth` (with `authKeyId` + `authSecret`),
or `AccessKeySecret` (with `authHeaderName`/`authKeyId` + `authSecretHeaderName`/`authSecret`). An
embedding endpoint may set `maxInputTokens` to override the token budget used when chunking oversized
memories (0 = resolve the budget automatically from the API format and model name).

An endpoint's `kind` is `Embedding`, `Inference`, or `Rerank` (ids are prefixed `eep_`, `iep_`, and `rep_`). A
**Rerank** endpoint is a cross-encoder that scores how well each candidate answers the query. Its `apiFormat` is
`Tei` (Hugging Face Text Embeddings Inference: `POST {baseUrl}/rerank` with `query` and `texts`, health path
`/health`) or `Cohere` (`POST {baseUrl}/v1/rerank` with `model`, `query`, and `documents`, answering
`results[].relevance_score`; also served by vLLM, Jina, and other Cohere-compatible rerankers).

Chunking of oversized memories is transparent to the API and configured on the **scope**: `chunkingMode`
(`OnOverflow` default — split only when a body exceeds the budget — `Always`, or `Off`), `chunkStrategy`
(`FixedTokenCount` default), `chunkMaxTokens` (0 = use the model budget), and `chunkOverlapTokens` (64).
A memory that overflows is embedded as several chunks under the hood; upsert, read, search, and delete all
continue to operate on the whole memory, and search returns one hit per memory regardless of chunking.

Reranking is also configured on the **scope**: `rerankEndpointId` (a `Rerank` endpoint in the tenant; null, the
default, turns reranking off), `rerankCandidates` (how many retrieved candidates the reranker scores before the top
`topK` are kept; 1 to 100, default 10), and `rerankMinScore` (drop reranked hits scoring below it, so a question with
no relevant memory returns nothing; null keeps every hit). Creating or updating a scope with a `rerankEndpointId` that
is missing or not a `Rerank` endpoint returns 400. `PUT` replaces the whole scope, so send every field you want to
keep (read the scope first).

A memory upsert accepts `supersedes`, a list of slugs (or `mem_` ids) of memories in the same scope that the new
memory replaces, for example an earlier decision it reverses. The server keeps `supersededBy` (the id of the replacing
memory) on each replaced memory; it is read-only. Removing a slug from `supersedes`, or deleting the replacing memory,
makes the replaced memory current again, and a memory written after the memory that replaces it is marked replaced
when it is created. On scopes with semantic search, the upsert response also carries `similarMemories`: existing
memories whose embedding is very close to the new one (`id`, `slug`, `title`, `categoryId`, `similarity`), so the
writer can reuse that slug or supersede it instead of keeping a duplicate. The field is omitted when there are none.
The threshold is the server setting `retrieval.duplicateSimilarityThreshold` (default 0.85). The threshold depends on the embedding model: with all-minilm, a memory and its replacement typically score 0.55 to 0.88, while distinct but closely related memories can reach 0.89, so treat the list as candidates to review.

A search body is
`{ "queryText": "…", "mode": "Hybrid", "topK": 10, "categoryFilter": "…", "tokenBudget": 240, "minScore": null, "recencyWeight": 0.1, "superseded": "Demote", "linkExpansion": 0, "diversity": 0, "rerank": null, "minRerankScore": null }`.
`queryText` is required (an empty or missing query returns 400). `categoryFilter` accepts a category name or
its `cat_` id; an unknown category returns 400. `minScore` (optional) drops hits scoring below it.
`recencyWeight` (hybrid only, 0 to 1, default 0.1, 0 disables) adds a signal that favors more recently written
memories, which mostly breaks near-ties such as a fact and its later replacement. Recency is measured by when a
memory was last written, so for scopes filled by a bulk import, where write order carries no meaning, pass
`recencyWeight: 0`.

`superseded` controls memories that another memory replaces: `Demote` (default) ranks each replaced memory
directly after its replacement, adding the replacement when the search did not retrieve it; `Hide` drops replaced
memories (still adding their replacement); `Include` leaves the order unchanged. Replacement chains are followed to
the current memory. `linkExpansion` (0 to 10, default 0) adds up to that many memories linked from the results,
through a memory's `links` list or `[[slug]]` references in its body, placed right after the result that links to
them; they are extra to `topK`. `diversity` (0 to 1, default 0) reorders the results by maximal marginal relevance,
so a result that mostly repeats a higher-ranked one (by word overlap) gives way to other relevant memories.

When the scope has a rerank endpoint, the search retrieves `rerankCandidates` candidates, has the reranker score
the title and up to 1,200 characters of each, and keeps the best `topK`. `rerank: false` skips it for one query, and
`rerank: true` fails with 400 when the scope has no rerank endpoint. `minRerankScore` drops reranked hits below it,
overriding the scope's `rerankMinScore`. `minScore` still applies to the retrieval score before reranking. The
response's `reranked` is true when the hits were reranked.

Each hit has `storeKey`, `slug`, `title`, `snippet`, and `score`, plus the evidence behind the score: `vectorScore`
and `textScore` (the raw leg scores, null when that leg did not return the hit), and in hybrid mode `vectorRank`
and `textRank` (1-based ranks in each leg). The meaning of `score` depends on the mode. In `Hybrid` it is the fused
reciprocal-rank score normalized to 0..1 (1.0 means ranked first by every signal). In `Semantic` it is the vector
similarity, and in `Keyword` the store's text relevance. When the search was reranked, `score` and `rerankScore`
hold the reranker's score (0..1 for TEI and Cohere-compatible rerankers). Hits also carry `memoryId`, `supersededBy`
(the slug of the memory that replaces this one, null when current), and `linkedFrom` (the slug of the result that
brought this memory in by link expansion or as a replacement, null for directly retrieved memories).

Chat (`POST …/scopes/{scopeId}/chat`) takes `{ "question": "…", "topK": 0, "inferenceEndpointId": "…" }`. A `topK` of 0
(the default) retrieves the server's default number of memories, 8. Chat follows up to 2 links from the retrieved
memories (server setting `retrieval.chatLinkExpansion`), tells the model which memories are outdated, and, when the
scope's reranker rejects every candidate, grounds the answer on no memories so the model says the answer is not in
memory.

### Instructions (tenant-scoped)

Tenant-wide standing guidance surfaced to agents. Reads are open to any principal in the
tenant; writes require `IsAdmin` or `IsTenantAdmin`. Instructions are returned in ascending
`position` order.

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/v1.0/api/tenants/{tenantId}/instructions` | tenant | List instructions (ascending `position`) |
| POST | `/v1.0/api/tenants/{tenantId}/instructions` | admin / tenant admin | Create an instruction |
| GET | `/v1.0/api/tenants/{tenantId}/instructions/{instructionId}` | tenant | Read an instruction |
| PUT | `/v1.0/api/tenants/{tenantId}/instructions/{instructionId}` | admin / tenant admin | Update an instruction |
| DELETE | `/v1.0/api/tenants/{tenantId}/instructions/{instructionId}` | admin / tenant admin | Delete an instruction |

Create/update body:

```json
{ "name": "House style", "content": "Prefer terse commit messages.", "position": 0, "active": true }
```

### Batch operations

Most resources expose uniform batch endpoints for bulk retrieval, creation, and deletion. All
are **POST** with a JSON body:

- **batch-get** / **batch-delete** body: `{ "ids": ["…", "…"] }`
- **batch** (create/upsert) body: `{ "items": [ <object>, … ] }`

Responses:

- batch-get / batch → `{ "objects": [ … ] }`
- batch-delete → `{ "deleted": <int> }`

Which resources support which endpoints:

| Resource | Path prefix | batch-get | batch (create/upsert) | batch-delete |
| --- | --- | --- | --- | --- |
| Instructions | `…/instructions` | ✓ | ✓ | ✓ |
| Endpoints | `…/endpoints` | ✓ | ✓ | ✓ |
| Memories | `…/scopes/{scopeId}/memories` | ✓ | ✓ (upsert) | ✓ (cleans store content) |
| Scopes | `…/scopes` | ✓ | — | ✓ (cascades) |
| Categories | `…/scopes/{scopeId}/categories` | ✓ | — | ✓ (cascades) |
| Users | `…/users` | ✓ | — | ✓ (cascades per user) |
| Credentials | `…/credentials` | ✓ | — | ✓ |

For example: `POST …/instructions/batch-get`, `POST …/instructions/batch`,
`POST …/instructions/batch-delete`. batch-delete honors the same cascade rules as the
corresponding single-resource `DELETE`.

### Request history

Captured best-effort for every routed request (health excluded). A system administrator sees
all tenants; a tenant principal sees only its own tenant's requests.

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/v1.0/api/requests` | session or credential | List request history |
| GET | `/v1.0/api/requests/{id}` | session or credential | Read a single request record |
| DELETE | `/v1.0/api/requests` | session or credential | Clear request history |

### Server settings (system administrator only)

Read and modify the running server's settings file. Request-history changes take effect
immediately; changes to other sections require a restart, signalled by `restartRequired: true`
in the `PUT` response.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/v1.0/api/settings` | Return `{ settings, settingsFile, liveSections }` |
| PUT | `/v1.0/api/settings` | Persist a full settings body to the settings file |
| POST | `/v1.0/api/settings/restart` | Exit the node so the container relaunches it with the saved settings |

`POST /v1.0/api/settings/restart` exits the process; under Docker (`restart: unless-stopped`)
the node is relaunched automatically with the persisted settings.

---

## Response codes

| Code | Meaning |
| --- | --- |
| 200 | Success |
| 201 | Created |
| 204 | No content (delete / logout) |
| 400 | Bad request |
| 401 | Unauthorized (authentication failed) |
| 403 | Forbidden (authorization denied) |
| 404 | Not found |
| 409 | Conflict (already exists) |

Error bodies are shaped `{ "error": "<code>", "message": "<human readable>" }`.
