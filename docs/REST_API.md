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

### 2. Credential access key + secret key (automation, MCP, agents)

Send **both** headers on every request:

```
x-access-key: isisdefaultkey
x-secret-key: isisdefaultsecret
```

Both are required. A credential resolves to its owning user's tenant and inherits the owner's `IsAdmin` / `IsTenantAdmin` flags. The raw secret key is shown only once, at creation.

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
| POST | `/v1.0/api/tenants` | Create a tenant |
| GET | `/v1.0/api/tenants/{tenantId}` | Read a tenant |
| PUT | `/v1.0/api/tenants/{tenantId}` | Update a tenant |
| DELETE | `/v1.0/api/tenants/{tenantId}` | Delete a tenant |

### Users (tenant administrator or system administrator)

Passwords are hashed server-side and never returned.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/v1.0/api/tenants/{tenantId}/users` | List users |
| POST | `/v1.0/api/tenants/{tenantId}/users` | Create a user |
| GET | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Read a user |
| PUT | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Update a user |
| DELETE | `/v1.0/api/tenants/{tenantId}/users/{userId}` | Delete a user (cascades to owned credentials) |

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
| GET/PUT/DELETE | `/v1.0/api/tenants/{tenantId}/scopes/{scopeId}` | Read / update / delete a scope |
| GET/POST | `…/scopes/{scopeId}/categories` | List / create categories |
| GET/PUT/DELETE | `…/scopes/{scopeId}/categories/{categoryId}` | Read / update / delete a category |
| GET/POST | `…/scopes/{scopeId}/memories` | List / upsert memories |
| GET/DELETE | `…/scopes/{scopeId}/memories/{memoryId}` | Read / delete a memory |
| POST | `…/scopes/{scopeId}/memories/search` | Keyword / semantic / hybrid search |
| POST | `…/scopes/{scopeId}/chat` | Chat with memory |
| GET | `…/scopes/{scopeId}/guide` | Agent onboarding manifest |
| GET/POST | `…/endpoints` | List / create model endpoints |
| GET/PUT/DELETE | `…/endpoints/{endpointId}` | Read / update / delete an endpoint |
| GET | `…/endpoint-health` | Aggregate endpoint health |
| GET/POST/DELETE | `…/collections` | RecallDB collections pass-through |
| GET | `/v1.0/api/requests` | Request history |

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
