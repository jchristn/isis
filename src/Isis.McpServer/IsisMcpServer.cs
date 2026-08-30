namespace Isis.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core;
    using Isis.McpServer.Settings;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// The Isis MCP server. Authenticates the caller from the MCP transport headers and proxies each tool
    /// call to the Isis REST API over loopback, forwarding the caller's credentials so the REST server
    /// performs the authoritative authentication and tenant scoping.
    /// </summary>
    public class IsisMcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Effective settings.
        /// </summary>
        public McpServerSettings Settings { get; }

        #endregion

        #region Private-Members

        private readonly AsyncLocal<McpCallerCredentials?> _Caller = new AsyncLocal<McpCallerCredentials?>();
        private readonly HttpClient _RestClient;
        private readonly McpHttpServer _Server;
        private CancellationTokenSource? _Cts;
        private Task? _ServerTask;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the MCP server.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public IsisMcpServer(McpServerSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _RestClient = new HttpClient();
            _RestClient.BaseAddress = new Uri(settings.RestBaseUrl());

            _Server = new McpHttpServer(settings.Hostname, settings.Port, settings.RpcPath, settings.EventsPath, true, settings.McpPath);
            _Server.ServerName = "Isis.McpServer";
            _Server.ServerVersion = Constants.ProductVersion;
            _Server.EnableCors = true;
            _Server.AuthenticationHandler = AuthenticateAsync;

            RegisterTools();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the MCP server.
        /// </summary>
        /// <param name="token">Cancellation token linked to the server lifetime.</param>
        public void Start(CancellationToken token = default)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _ServerTask = _Server.StartAsync(_Cts.Token);
        }

        /// <summary>
        /// Stop the MCP server.
        /// </summary>
        public void Stop()
        {
            try
            {
                _Cts?.Cancel();
                _Server.Stop();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Proxy a request to the Isis REST API and return a structured envelope. Exposed for testing.
        /// </summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="path">The REST path (beginning with a slash).</param>
        /// <param name="jsonBody">The JSON request body, or null.</param>
        /// <param name="tool">The tool name, echoed in the envelope.</param>
        /// <param name="credentials">The caller credentials to forward.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An envelope with success, statusCode, tool, and data.</returns>
        public async Task<object> ProxyAsync(HttpMethod method, string path, string? jsonBody, string tool, McpCallerCredentials credentials, CancellationToken token = default)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));

            using HttpRequestMessage request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(credentials.AccessKey)) request.Headers.Add("x-access-key", credentials.AccessKey);
            if (!string.IsNullOrEmpty(credentials.SecretKey)) request.Headers.Add("x-secret-key", credentials.SecretKey);
            if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _RestClient.SendAsync(request, token).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            Dictionary<string, object?> envelope = new Dictionary<string, object?>();
            envelope["tool"] = tool;
            envelope["success"] = response.IsSuccessStatusCode;
            envelope["statusCode"] = (int)response.StatusCode;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    envelope["data"] = JsonSerializer.Deserialize<JsonElement>(text);
                }
                catch (JsonException)
                {
                    envelope["data"] = text;
                }
            }

            return envelope;
        }

        /// <summary>
        /// Dispose the server.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { _Server.Dispose(); } catch { }
            try { _Cts?.Dispose(); } catch { }
            _RestClient.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private Task<AuthenticationResult> AuthenticateAsync(HttpListenerRequest request)
        {
            McpCallerCredentials credentials = new McpCallerCredentials();
            credentials.AccessKey = ReadAccessKey(request);
            credentials.SecretKey = request.Headers["x-secret-key"];

            if (!credentials.HasAny())
            {
                AuthenticationResult failure = new AuthenticationResult();
                failure.IsAuthenticated = false;
                failure.StatusCode = 401;
                failure.ErrorMessage = "Provide the tenant credential access key as an 'Authorization: Bearer <accessKey>' token (or the 'x-access-key' header).";
                return Task.FromResult(failure);
            }

            _Caller.Value = credentials;

            AuthenticationResult success = new AuthenticationResult();
            success.IsAuthenticated = true;
            success.Principal = "credential";
            return Task.FromResult(success);
        }

        /// <summary>
        /// Resolve the caller's access key from either an <c>Authorization: Bearer &lt;accessKey&gt;</c> token
        /// (the single-credential form supported by MCP clients such as Mux, which cannot send two headers) or
        /// the <c>x-access-key</c> header. The access key is the public, transferable material; the secret is
        /// never sent as a bearer token and stays client-side.
        /// </summary>
        private static string? ReadAccessKey(HttpListenerRequest request)
        {
            string? authorization = request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string token = authorization.Substring("Bearer ".Length).Trim();
                if (!string.IsNullOrEmpty(token)) return token;
            }

            return request.Headers["x-access-key"];
        }

        private McpCallerCredentials CurrentCredentials()
        {
            return _Caller.Value ?? new McpCallerCredentials();
        }

        private static string Require(RpcParameters? parameters, string name)
        {
            string? value = parameters?.GetString(name);
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Argument '" + name + "' is required.");
            return value;
        }

        private static string Encode(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private void RegisterTools()
        {
            _Server.RegisterTool(
                "whoami",
                "The product is named Isis (proper noun; write it 'Isis' or 'isis' — NEVER the all-caps 'ISIS', which is a different thing entirely and must not be used). "
                + "Resolve the tenant and principal the caller's credential maps to. Call this FIRST to discover your tenantId, then call instructions for this tenant's standing guidance. "
                + "IMPORTANT — tenantId is required on EVERY other Isis tool call (scope, category, memory, guide, endpoint, and instructions tools all take a tenantId argument): this whoami call is the ONLY one that does not need it, and its response gives you the tenantId to pass to all the others. If you omit tenantId elsewhere the call fails; always thread the tenantId from this response into subsequent calls. "
                + "Authentication: every call to this Isis MCP server is authenticated with a tenant credential ACCESS KEY, presented as a bearer token — your MCP client sends 'Authorization: Bearer <accessKey>' (the 'x-access-key' header is also accepted). The access key is the public, transferable material; the secret key is NEVER sent to the MCP server and stays client-side. Obtain an access key from an Isis administrator; the local-dev default is 'isisdefaultkey'. Requests without an access key are rejected with HTTP 401. Because the access key alone authenticates an MCP caller, treat it as a capability token and scope it least-privilege.",
                new { type = "object", properties = new { } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/whoami", null, "whoami", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "instructions",
                "Get this tenant's standing instructions for how to use its memory — conventions, house rules, and guidance authored by the tenant. Call this after whoami. Required: tenantId.",
                new { type = "object", properties = new { tenantId = new { type = "string", description = "Tenant identifier." } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/instructions", null, "instructions", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "scope_enumerate",
                "List the memory scopes in a tenant. Required: tenantId.",
                new { type = "object", properties = new { tenantId = new { type = "string", description = "Tenant identifier." } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes", null, "scope_enumerate", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "scope_create",
                "Create a memory scope for a project when one does not already exist (check first with scope_enumerate). "
                + "Required: tenantId, name. Optional: description; storeProvider — RecallDb (default: semantic + keyword, needs an embedding endpoint), Verbex (keyword-only), or Filesystem (keyword-only, git-trackable files). "
                + "For RecallDb you may pass embeddingEndpointId and dimensionality, but if you omit them the tenant's embedding endpoint and its dimensionality are selected AUTOMATICALLY (list options with endpoint_enumerate). "
                + "If the tenant has NO embedding endpoint, RecallDb is rejected with guidance — use storeProvider Filesystem or Verbex instead. Filesystem also accepts filesystemLayout (SingleFile|Hierarchy|OkfBundle — OkfBundle writes a git-trackable Open Knowledge Format bundle: one markdown file per memory with YAML frontmatter plus a generated index.md) and targetPath.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        name = new { type = "string", description = "Unique scope name within the tenant (e.g. the project name)." },
                        description = new { type = "string" },
                        storeProvider = new { type = "string", description = "RecallDb, Verbex, or Filesystem. Defaults to RecallDb." },
                        embeddingEndpointId = new { type = "string", description = "Embedding endpoint id for RecallDb semantic scopes." },
                        dimensionality = new { type = "integer", description = "Embedding vector dimension for RecallDb scopes." },
                        filesystemLayout = new { type = "string", description = "SingleFile, Hierarchy, or OkfBundle (Open Knowledge Format), for Filesystem scopes." },
                        targetPath = new { type = "string", description = "Directory or file path, for Filesystem scopes." }
                    },
                    required = new[] { "tenantId", "name" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    if (p?.GetString("description") != null) body["description"] = p.GetString("description");
                    if (p?.GetString("storeProvider") != null) body["storeProvider"] = p.GetString("storeProvider");
                    if (p?.GetString("embeddingEndpointId") != null) body["embeddingEndpointId"] = p.GetString("embeddingEndpointId");
                    long? dimensionality = p?.GetInt64("dimensionality");
                    if (dimensionality.HasValue) body["dimensionality"] = dimensionality.Value;
                    if (p?.GetString("filesystemLayout") != null) body["filesystemLayout"] = p.GetString("filesystemLayout");
                    if (p?.GetString("targetPath") != null) body["targetPath"] = p.GetString("targetPath");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "scope_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "endpoint_enumerate",
                "List the tenant's configured model endpoints (embedding and inference), each with its id, kind, model, and embedding dimensionality. Use this to find an embeddingEndpointId (and its dimensionality) BEFORE creating a RecallDb semantic scope. If no embedding endpoint is listed, create Filesystem or Verbex (keyword-only) scopes instead. Required: tenantId. Optional: kind (Embedding or Inference).",
                new { type = "object", properties = new { tenantId = new { type = "string" }, kind = new { type = "string", description = "Optional filter: Embedding or Inference." } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoints";
                    string? kind = p?.GetString("kind");
                    if (!string.IsNullOrEmpty(kind)) path += "?kind=" + Encode(kind);
                    return await ProxyAsync(HttpMethod.Get, path, null, "endpoint_enumerate", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "guide",
                "Get the operating guide for a scope: its categories, their usage instructions, and store capabilities. Call this first. Required: tenantId, scopeId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" } }, required = new[] { "tenantId", "scopeId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/guide", null, "guide", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "category_enumerate",
                "List categories in a scope, including their usage instructions. Required: tenantId, scopeId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" } }, required = new[] { "tenantId", "scopeId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/categories", null, "category_enumerate", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "category_create",
                "Create a category in a scope. Required: tenantId, scopeId, name. Optional: description, instructions.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        name = new { type = "string", description = "Category name (also used as the RecallDB label)." },
                        description = new { type = "string" },
                        instructions = new { type = "string", description = "When and how to write memories in this category." }
                    },
                    required = new[] { "tenantId", "scopeId", "name" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    if (p?.GetString("description") != null) body["description"] = p.GetString("description");
                    if (p?.GetString("instructions") != null) body["instructions"] = p.GetString("instructions");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/categories";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "category_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_enumerate",
                "List memory summaries in a scope. Required: tenantId, scopeId. Optional: category (categoryId filter), maxResults.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        category = new { type = "string", description = "Optional filter by category ID (the cat_ id, not the name)." },
                        maxResults = new { type = "integer" }
                    },
                    required = new[] { "tenantId", "scopeId" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories";
                    List<string> queryParts = new List<string>();
                    string? category = p?.GetString("category");
                    if (!string.IsNullOrEmpty(category)) queryParts.Add("category=" + Encode(category));
                    long? maxResults = p?.GetInt64("maxResults");
                    if (maxResults.HasValue) queryParts.Add("maxResults=" + maxResults.Value);
                    if (queryParts.Count > 0) path += "?" + string.Join("&", queryParts);
                    return await ProxyAsync(HttpMethod.Get, path, null, "memory_enumerate", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_read",
                "Read a single memory by id. Required: tenantId, scopeId, memoryId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, memoryId = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "memoryId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories/" + Encode(Require(p, "memoryId")), null, "memory_read", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "memory_upsert",
                "Create or update a memory. Idempotent on (scope, category, slug). Required: tenantId, scopeId, categoryId, slug, body. Optional: title, summary, type.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        categoryId = new { type = "string" },
                        slug = new { type = "string", description = "Stable, link-addressable slug; re-writing updates in place." },
                        title = new { type = "string" },
                        summary = new { type = "string", description = "One-line recall hook." },
                        body = new { type = "string", description = "The memory content." },
                        type = new { type = "string", description = "Optional classification; one of User, Feedback, Project, Reference. Unknown or omitted values default to Project." }
                    },
                    required = new[] { "tenantId", "scopeId", "categoryId", "slug", "body" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["categoryId"] = Require(p, "categoryId");
                    body["slug"] = Require(p, "slug");
                    body["body"] = Require(p, "body");
                    if (p?.GetString("title") != null) body["title"] = p.GetString("title");
                    if (p?.GetString("summary") != null) body["summary"] = p.GetString("summary");
                    if (p?.GetString("type") != null) body["type"] = p.GetString("type");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "memory_upsert", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_search",
                "Search a scope's memory. Required: tenantId, scopeId, queryText. Optional: mode (Keyword|Semantic|Hybrid), topK, category.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        queryText = new { type = "string" },
                        mode = new { type = "string", description = "Keyword, Semantic, or Hybrid. Semantic/Hybrid require a RecallDB scope." },
                        topK = new { type = "integer" },
                        categoryName = new { type = "string", description = "Optional filter by category NAME (search filters by name; enumerate/read use the cat_ id)." }
                    },
                    required = new[] { "tenantId", "scopeId", "queryText" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["queryText"] = Require(p, "queryText");
                    if (p?.GetString("mode") != null) body["mode"] = p.GetString("mode");
                    long? topK = p?.GetInt64("topK");
                    if (topK.HasValue) body["topK"] = topK.Value;
                    if (p?.GetString("categoryName") != null) body["categoryFilter"] = p.GetString("categoryName");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories/search";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "memory_search", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_delete",
                "Delete a memory by id. Required: tenantId, scopeId, memoryId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, memoryId = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "memoryId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories/" + Encode(Require(p, "memoryId")), null, "memory_delete", CurrentCredentials(), ct).ConfigureAwait(false));
        }

        #endregion
    }
}
