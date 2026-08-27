/**
 * Isis API Client
 *
 * A single hand-rolled fetch-based client over the Isis REST API. There is no
 * axios dependency and no second HTTP abstraction anywhere in the dashboard.
 *
 * Auth model: the dashboard logs in with email + password and receives a
 * revocable session token, which is sent on every request as:
 *   - `Authorization: Bearer <token>`
 * Tenant is carried in the URL path (`/tenants/{tenantId}/...`). The token is
 * tenant-bound server-side. Credential (access-key/secret-key) auth exists for
 * automation/MCP but the dashboard itself never uses it.
 *
 * Base path for the management API is `/v1.0/api`. The OpenAPI document is
 * served from the server root at `/openapi.json`.
 *
 * Errors from the server are shaped `{ error, message }` with the HTTP status.
 * Enumeration responses are shaped:
 *   { maxResults, skip, totalRecords, recordsRemaining, endOfResults,
 *     continuationToken, objects: [...] }
 */

const API_BASE = '/v1.0/api';

/** Normalized error surface used across the UI. */
export class ApiError extends Error {
  constructor(status, message, body) {
    super(message || `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

/**
 * Normalize an Isis enumeration payload into a predictable table shape.
 * Falls back gracefully if the server returns a bare array.
 */
export function normalizePaged(payload) {
  if (Array.isArray(payload)) {
    return {
      items: payload,
      totalRecords: payload.length,
      skip: 0,
      maxResults: payload.length,
      recordsRemaining: 0,
      endOfResults: true,
      continuationToken: null
    };
  }
  const objects = payload?.objects || payload?.data || [];
  return {
    items: objects,
    totalRecords: payload?.totalRecords ?? objects.length,
    skip: payload?.skip ?? 0,
    maxResults: payload?.maxResults ?? objects.length,
    recordsRemaining: payload?.recordsRemaining ?? 0,
    endOfResults: payload?.endOfResults ?? true,
    continuationToken: payload?.continuationToken ?? null
  };
}

class ApiClient {
  /**
   * @param {string} baseUrl - Server base URL (e.g. http://127.0.0.1:8700)
   * @param {object} auth - { token: string, tenantId: string }
   */
  constructor(baseUrl, auth = {}) {
    this.baseUrl = (baseUrl || '').replace(/\/$/, '');
    this.token = auth.token || '';
    this.tenantId = auth.tenantId || 'ten_default';
  }

  _headers(extra = {}) {
    const headers = { 'Content-Type': 'application/json', ...extra };
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;
    return headers;
  }

  _url(path, query) {
    const url = new URL(this.baseUrl + path);
    if (query) {
      for (const [k, v] of Object.entries(query)) {
        if (v !== undefined && v !== null && v !== '') url.searchParams.append(k, v);
      }
    }
    return url.toString();
  }

  /** Core request. Returns parsed JSON on success; throws ApiError on failure. */
  async _request(method, path, { query = null, body = null, headers = {} } = {}) {
    let response;
    try {
      response = await fetch(this._url(path, query), {
        method,
        headers: this._headers(headers),
        body: body !== null && body !== undefined ? JSON.stringify(body) : undefined
      });
    } catch (networkErr) {
      throw new ApiError(0, `Network error: ${networkErr.message}`, null);
    }

    if (response.status === 401 || response.status === 403) {
      window.dispatchEvent(new CustomEvent('auth:unauthorized', { detail: { status: response.status } }));
    }

    const text = await response.text();
    let parsed = null;
    if (text) {
      try {
        parsed = JSON.parse(text);
      } catch {
        parsed = text;
      }
    }

    if (!response.ok) {
      const message =
        (parsed && (parsed.message || parsed.error)) || response.statusText || `HTTP ${response.status}`;
      throw new ApiError(response.status, message, parsed);
    }

    return parsed;
  }

  get(path, query, headers) {
    return this._request('GET', path, { query, headers });
  }
  post(path, body, query) {
    return this._request('POST', path, { body, query });
  }
  put(path, body) {
    return this._request('PUT', path, { body });
  }
  del(path) {
    return this._request('DELETE', path);
  }

  // ------------------------------------------------------------------
  // Health / server / identity
  // ------------------------------------------------------------------

  /** Liveness probe used to validate a pasted key. */
  health() {
    return this.get(`${API_BASE}/health`);
  }
  serverInfo() {
    return this.get(`${API_BASE}/server/info`);
  }
  whoami() {
    return this.get(`${API_BASE}/whoami`);
  }
  /** Root-served OpenAPI document (drives the API Explorer). */
  getOpenApiSpec() {
    return this.get('/openapi.json');
  }

  // ------------------------------------------------------------------
  // Authentication (email/password → session token)
  // ------------------------------------------------------------------

  /** Pre-auth: list the tenants an email address belongs to. Returns [{ id, name }]. */
  tenantsForEmail(email) {
    return this.post(`${API_BASE}/tenants-for-email`, { email }).then((r) => r?.tenants || []);
  }
  /** Pre-auth: exchange email/password/tenant for a session token. */
  login(email, password, tenantId) {
    return this.post(`${API_BASE}/token`, { email, password, tenantId });
  }
  /** Revoke the current session token (logout). Best-effort. */
  logout() {
    return this.del(`${API_BASE}/token`);
  }

  // ------------------------------------------------------------------
  // Tenants (administration)
  // ------------------------------------------------------------------

  listTenants(query = {}) {
    return this.get(`${API_BASE}/tenants`, query).then(normalizePaged);
  }
  getTenant(id) {
    return this.get(`${API_BASE}/tenants/${encodeURIComponent(id)}`);
  }
  createTenant(body) {
    return this.post(`${API_BASE}/tenants`, body);
  }
  updateTenant(id, body) {
    return this.put(`${API_BASE}/tenants/${encodeURIComponent(id)}`, body);
  }
  deleteTenant(id) {
    return this.del(`${API_BASE}/tenants/${encodeURIComponent(id)}`);
  }

  // ------------------------------------------------------------------
  // Users (tenant-scoped administration)
  // ------------------------------------------------------------------

  _tid(tid) {
    return encodeURIComponent(tid || this.tenantId);
  }

  listUsers(tid, query = {}) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/users`, query).then(normalizePaged);
  }
  getUser(tid, uid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/users/${encodeURIComponent(uid)}`);
  }
  createUser(tid, body) {
    return this.post(`${API_BASE}/tenants/${this._tid(tid)}/users`, body);
  }
  updateUser(tid, uid, body) {
    return this.put(`${API_BASE}/tenants/${this._tid(tid)}/users/${encodeURIComponent(uid)}`, body);
  }
  deleteUser(tid, uid) {
    return this.del(`${API_BASE}/tenants/${this._tid(tid)}/users/${encodeURIComponent(uid)}`);
  }

  // ------------------------------------------------------------------
  // Credentials (tenant-scoped administration; secret shown once on create)
  // ------------------------------------------------------------------

  listCredentials(tid, query = {}) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/credentials`, query).then(normalizePaged);
  }
  getCredential(tid, cid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/credentials/${encodeURIComponent(cid)}`);
  }
  createCredential(tid, body) {
    return this.post(`${API_BASE}/tenants/${this._tid(tid)}/credentials`, body);
  }
  updateCredential(tid, cid, body) {
    return this.put(`${API_BASE}/tenants/${this._tid(tid)}/credentials/${encodeURIComponent(cid)}`, body);
  }
  deleteCredential(tid, cid) {
    return this.del(`${API_BASE}/tenants/${this._tid(tid)}/credentials/${encodeURIComponent(cid)}`);
  }

  // ------------------------------------------------------------------
  // Scopes
  // ------------------------------------------------------------------

  listScopes(tid, query = {}) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/scopes`, query).then(normalizePaged);
  }
  getScope(tid, sid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}`);
  }
  createScope(tid, body) {
    return this.post(`${API_BASE}/tenants/${this._tid(tid)}/scopes`, body);
  }
  updateScope(tid, sid, body) {
    return this.put(`${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}`, body);
  }
  deleteScope(tid, sid) {
    return this.del(`${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}`);
  }

  // ------------------------------------------------------------------
  // Categories
  // ------------------------------------------------------------------

  listCategories(tid, sid, query = {}) {
    return this.get(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/categories`,
      query
    ).then(normalizePaged);
  }
  getCategory(tid, sid, cid) {
    return this.get(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/categories/${encodeURIComponent(cid)}`
    );
  }
  createCategory(tid, sid, body) {
    return this.post(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/categories`,
      body
    );
  }
  updateCategory(tid, sid, cid, body) {
    return this.put(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/categories/${encodeURIComponent(cid)}`,
      body
    );
  }
  deleteCategory(tid, sid, cid) {
    return this.del(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/categories/${encodeURIComponent(cid)}`
    );
  }

  // ------------------------------------------------------------------
  // Memories
  // ------------------------------------------------------------------

  listMemories(tid, sid, query = {}) {
    return this.get(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/memories`,
      query
    ).then(normalizePaged);
  }
  getMemory(tid, sid, mid) {
    return this.get(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/memories/${encodeURIComponent(mid)}`
    );
  }
  upsertMemory(tid, sid, body) {
    return this.post(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/memories`,
      body
    );
  }
  deleteMemory(tid, sid, mid) {
    return this.del(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/memories/${encodeURIComponent(mid)}`
    );
  }
  searchMemories(tid, sid, body) {
    return this.post(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/memories/search`,
      body
    );
  }

  // ------------------------------------------------------------------
  // Guide (agent onboarding manifest)
  // ------------------------------------------------------------------

  getGuide(tid, sid) {
    return this.get(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/guide`
    );
  }

  // ------------------------------------------------------------------
  // Chat with Memory
  // ------------------------------------------------------------------

  chat(tid, sid, body) {
    return this.post(
      `${API_BASE}/tenants/${this._tid(tid)}/scopes/${encodeURIComponent(sid)}/chat`,
      body
    );
  }

  // ------------------------------------------------------------------
  // Model endpoints (embedding + inference) + health
  // ------------------------------------------------------------------

  listEndpoints(tid, kind, query = {}) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/endpoints`, { kind, ...query }).then(
      normalizePaged
    );
  }
  getEndpoint(tid, eid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/endpoints/${encodeURIComponent(eid)}`);
  }
  createEndpoint(tid, body) {
    return this.post(`${API_BASE}/tenants/${this._tid(tid)}/endpoints`, body);
  }
  updateEndpoint(tid, eid, body) {
    return this.put(`${API_BASE}/tenants/${this._tid(tid)}/endpoints/${encodeURIComponent(eid)}`, body);
  }
  deleteEndpoint(tid, eid) {
    return this.del(`${API_BASE}/tenants/${this._tid(tid)}/endpoints/${encodeURIComponent(eid)}`);
  }
  /** Aggregate health snapshot for all endpoints (probes performed server-side). */
  endpointHealth(tid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/endpoint-health`);
  }

  // ------------------------------------------------------------------
  // RecallDB collections (pass-through to RecallDB; requires a configured
  // RecallDB endpoint — returns 400 RecallDbNotConfigured otherwise).
  // ------------------------------------------------------------------

  listCollections(tid, query = {}) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/collections`, query);
  }
  getCollection(tid, cid) {
    return this.get(`${API_BASE}/tenants/${this._tid(tid)}/collections/${encodeURIComponent(cid)}`);
  }
  createCollection(tid, body) {
    return this.post(`${API_BASE}/tenants/${this._tid(tid)}/collections`, body);
  }
  deleteCollection(tid, cid) {
    return this.del(`${API_BASE}/tenants/${this._tid(tid)}/collections/${encodeURIComponent(cid)}`);
  }

  // ------------------------------------------------------------------
  // Request history
  // ------------------------------------------------------------------

  getRequestHistory(query = {}) {
    return this.get(`${API_BASE}/requests`, query).then(normalizePaged);
  }
  getRequestHistoryEntry(id) {
    return this.get(`${API_BASE}/requests/${encodeURIComponent(id)}`);
  }

  // ------------------------------------------------------------------
  // API Explorer raw execution — returns the raw Response so the caller
  // can inspect status, headers, and streaming bodies.
  // ------------------------------------------------------------------

  async executeExplorer({ method, path, query, headers, body }) {
    return fetch(this._url(path, query), {
      method,
      headers: this._headers(headers || {}),
      body: body !== null && body !== undefined && body !== '' ? body : undefined
    });
  }
}

export default ApiClient;
