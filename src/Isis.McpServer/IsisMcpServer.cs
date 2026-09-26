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

            // Voltaic 2.x always registers the MCP protocol methods (including ping); the diagnostic echo/getTime tools stay
            // off so tools/list carries only the Isis tools.
            _Server = new McpHttpServer(settings.Hostname, settings.Port, settings.RpcPath, settings.EventsPath, includeDiagnosticTools: false, mcpPath: settings.McpPath);
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

        private static List<Dictionary<string, string>>? ChatHistory(RpcParameters? parameters)
        {
            // Accept a JSON array of { role, content } objects; entries without content are skipped.
            string? raw = parameters?.RawJson;
            if (string.IsNullOrEmpty(raw)) return null;

            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("history", out JsonElement value)) return null;
            if (value.ValueKind != JsonValueKind.Array) return null;

            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string content = item.TryGetProperty("content", out JsonElement c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(content)) continue;
                string role = item.TryGetProperty("role", out JsonElement r) && r.ValueKind == JsonValueKind.String ? (r.GetString() ?? "user") : "user";
                result.Add(new Dictionary<string, string> { { "role", role }, { "content", content } });
            }

            return result;
        }

        private static List<string>? StringList(RpcParameters? parameters, string name)
        {
            // Accept a JSON array of strings, or a single comma-separated string from clients that flatten arrays.
            string? raw = parameters?.RawJson;
            if (string.IsNullOrEmpty(raw)) return null;

            using JsonDocument document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(name, out JsonElement value)) return null;

            List<string> result = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) result.Add(item.GetString()!.Trim());
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                foreach (string part in (value.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) result.Add(part);
            }
            else
            {
                return null;
            }

            return result;
        }

        private static void AddRerankSettings(RpcParameters? p, Dictionary<string, object?> body)
        {
            string? rerankEndpointId = p?.GetString("rerankEndpointId");
            if (rerankEndpointId != null) body["rerankEndpointId"] = rerankEndpointId.Length > 0 ? rerankEndpointId : null;
            long? rerankCandidates = p?.GetInt64("rerankCandidates");
            if (rerankCandidates.HasValue) body["rerankCandidates"] = rerankCandidates.Value;
            double? rerankMinScore = p?.GetDouble("rerankMinScore");
            if (rerankMinScore.HasValue) body["rerankMinScore"] = rerankMinScore.Value;
        }

        private static string BuildEndpointBody(RpcParameters? p)
        {
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["name"] = Require(p, "name");
            body["baseUrl"] = Require(p, "baseUrl");
            if (p?.GetString("kind") != null) body["kind"] = p.GetString("kind");
            if (p?.GetString("apiFormat") != null) body["apiFormat"] = p.GetString("apiFormat");
            if (p?.GetString("authType") != null) body["authType"] = p.GetString("authType");
            if (p?.GetString("authHeaderName") != null) body["authHeaderName"] = p.GetString("authHeaderName");
            if (p?.GetString("authSecretHeaderName") != null) body["authSecretHeaderName"] = p.GetString("authSecretHeaderName");
            if (p?.GetString("authQueryParam") != null) body["authQueryParam"] = p.GetString("authQueryParam");
            if (p?.GetString("authKeyId") != null) body["authKeyId"] = p.GetString("authKeyId");
            if (p?.GetString("authSecret") != null) body["authSecret"] = p.GetString("authSecret");
            if (p?.GetString("model") != null) body["model"] = p.GetString("model");
            long? dimensionality = p?.GetInt64("dimensionality");
            if (dimensionality.HasValue) body["dimensionality"] = dimensionality.Value;
            if (p?.GetString("healthCheckUrl") != null) body["healthCheckUrl"] = p.GetString("healthCheckUrl");
            bool? active = p?.GetBoolean("active");
            if (active.HasValue) body["active"] = active.Value;
            return JsonSerializer.Serialize(body);
        }

        private static object EndpointProperties()
        {
            return new
            {
                tenantId = new { type = "string" },
                endpointId = new { type = "string", description = "Endpoint id (update only)." },
                name = new { type = "string" },
                kind = new { type = "string", description = "Embedding, Inference, or Rerank." },
                apiFormat = new { type = "string", description = "Ollama, OpenAI, VLlm, or Gemini for embedding and inference; Tei or Cohere (or VLlm) for a cross-encoder rerank endpoint, or Ollama/OpenAI for a chat model used as a reranker." },
                baseUrl = new { type = "string", description = "Full base URL; the API path is appended (e.g. http://host:11434 or https://api.openai.com)." },
                authType = new { type = "string", description = "None, BearerToken, ApiKeyHeader, QueryParam, BasicAuth, or AccessKeySecret." },
                authHeaderName = new { type = "string", description = "Header name for ApiKeyHeader, or access-key header for AccessKeySecret." },
                authSecretHeaderName = new { type = "string", description = "Secret-key header name for AccessKeySecret." },
                authQueryParam = new { type = "string", description = "Query-string parameter name for QueryParam auth (e.g. key)." },
                authKeyId = new { type = "string", description = "Username (BasicAuth) or access key (AccessKeySecret)." },
                authSecret = new { type = "string", description = "Bearer token / header value / query value / password / secret key." },
                model = new { type = "string" },
                dimensionality = new { type = "integer", description = "Embedding vector dimension (embedding endpoints)." },
                healthCheckUrl = new { type = "string" },
                active = new { type = "boolean" }
            };
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
                "Get standing instructions for how to use memory — conventions, house rules, and guidance. Call this after whoami. Required: tenantId. Optional: scopeId — when provided, returns the scope's EFFECTIVE instructions (the tenant-global set with the scope's own instructions merged in: appended, overriding, or hiding by name); when omitted, returns the tenant-global set.",
                new { type = "object", properties = new { tenantId = new { type = "string", description = "Tenant identifier." }, scopeId = new { type = "string", description = "Optional scope identifier; resolves that scope's effective instructions." } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    string tenantId = Encode(Require(p, "tenantId"));
                    string? scopeId = p?.GetString("scopeId");
                    string path = string.IsNullOrEmpty(scopeId)
                        ? "/v1.0/api/tenants/" + tenantId + "/instructions"
                        : "/v1.0/api/tenants/" + tenantId + "/scopes/" + Encode(scopeId) + "/effective-instructions";
                    return await ProxyAsync(HttpMethod.Get, path, null, "instructions", CurrentCredentials(), ct).ConfigureAwait(false);
                });

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
                        targetPath = new { type = "string", description = "Directory or file path, for Filesystem scopes." },
                        chunkingMode = new { type = "string", description = "When to chunk oversized bodies for embedding: OnOverflow (default), Always, or Off." },
                        chunkStrategy = new { type = "string", description = "Chunk splitting strategy, for example FixedTokenCount (default), SentenceBased, ParagraphBased, Recursive." },
                        chunkMaxTokens = new { type = "integer", description = "Per-chunk token budget (0 = the embedding model's budget)." },
                        chunkOverlapTokens = new { type = "integer", description = "Token overlap between adjacent chunks (default 64)." },
                        rerankEndpointId = new { type = "string", description = "Optional Rerank endpoint id (rep_); searches in the scope are then reranked by default." },
                        rerankCandidates = new { type = "integer", description = "Candidates sent to the reranker (1..100, default 10)." },
                        rerankMinScore = new { type = "number", description = "Drop reranked hits scoring below this (0..1). Omit to keep all." }
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
                    if (p?.GetString("chunkingMode") != null) body["chunkingMode"] = p.GetString("chunkingMode");
                    if (p?.GetString("chunkStrategy") != null) body["chunkStrategy"] = p.GetString("chunkStrategy");
                    long? chunkMaxTokens = p?.GetInt64("chunkMaxTokens");
                    if (chunkMaxTokens.HasValue) body["chunkMaxTokens"] = chunkMaxTokens.Value;
                    long? chunkOverlapTokens = p?.GetInt64("chunkOverlapTokens");
                    if (chunkOverlapTokens.HasValue) body["chunkOverlapTokens"] = chunkOverlapTokens.Value;
                    AddRerankSettings(p, body);
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "scope_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "endpoint_enumerate",
                "List the tenant's configured model endpoints (embedding, inference, and rerank), each with its id, kind, model, and embedding dimensionality. Use this to find an embeddingEndpointId (and its dimensionality) BEFORE creating a RecallDb semantic scope. If no embedding endpoint is listed, create Filesystem or Verbex (keyword-only) scopes instead. Required: tenantId. Optional: kind (Embedding, Inference, or Rerank).",
                new { type = "object", properties = new { tenantId = new { type = "string" }, kind = new { type = "string", description = "Optional filter: Embedding, Inference, or Rerank." } }, required = new[] { "tenantId" } },
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
                        name = new { type = "string", description = "Category name (unique within the scope; accepted by memory_search as a filter)." },
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
                "Create or update a memory. Idempotent on (scope, category, slug). Required: tenantId, scopeId, categoryId, slug, body. Optional: title, summary, type, links, supersedes. "
                + "When the new memory replaces an older one (a changed decision, a corrected fact), pass the old slug in supersedes: search then ranks the old memory after the new one and marks it outdated. "
                + "The response lists existing memories that closely resemble this one in similarMemories; if one of them says the same thing, update it (reuse its slug) instead of keeping both, or supersede it.",
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
                        type = new { type = "string", description = "Optional classification; one of User, Feedback, Project, Reference. Unknown or omitted values default to Project." },
                        links = new { type = "array", items = new { type = "string" }, description = "Slugs of related memories. Chat follows these (and [[slug]] references in the body) to add linked context." },
                        supersedes = new { type = "array", items = new { type = "string" }, description = "Slugs of memories in this scope that this memory replaces." }
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
                    List<string>? links = StringList(p, "links");
                    if (links != null) body["links"] = links;
                    List<string>? supersedes = StringList(p, "supersedes");
                    if (supersedes != null) body["supersedes"] = supersedes;
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "memory_upsert", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_search",
                "Search a scope's memory. Required: tenantId, scopeId, queryText. Optional: mode (Keyword|Semantic|Hybrid), topK, categoryName, minScore, recencyWeight, superseded, linkExpansion, diversity, rerank, minRerankScore, additionalQueries, decompose. For a question about several distinct things, pass each part in additionalQueries (or set decompose) so every part's memories are found. "
                + "Hits replaced by a newer memory carry supersededBy (prefer the replacement); hits added by following links carry linkedFrom.",
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
                        categoryName = new { type = "string", description = "Optional category filter: the category's name or its cat_ id. An unknown category is an error, not an empty result." },
                        minScore = new { type = "number", description = "Optional minimum score; weaker hits are dropped. Hybrid scores are fused and normalized to 0..1." },
                        recencyWeight = new { type = "number", description = "Hybrid only: weight 0..1 of a signal favoring recently written memories (default 0.1; 0 disables)." },
                        superseded = new { type = "string", description = "Demote (default: a replaced memory ranks right after its replacement), Hide (drop replaced memories), or Include (unchanged order, only marked)." },
                        linkExpansion = new { type = "integer", description = "Add up to this many linked memories (0..10, default 0) after the results that link to them." },
                        diversity = new { type = "number", description = "0..1 (default 0): higher values drop results that repeat higher-ranked ones in favor of other relevant memories." },
                        rerank = new { type = "boolean", description = "Rerank with the scope's rerank endpoint. Default: rerank when the scope has one." },
                        minRerankScore = new { type = "number", description = "Drop reranked hits scoring below this (0..1). Defaults to the scope's rerankMinScore." },
                        additionalQueries = new { type = "array", items = new { type = "string" }, description = "Up to 4 extra queries searched alongside queryText and fused, for example the parts of a multi-part question." },
                        decompose = new { type = "boolean", description = "Have the tenant's inference model split a multi-part question into sub-queries before searching (default false)." }
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
                    double? minScore = p?.GetDouble("minScore");
                    if (minScore.HasValue) body["minScore"] = minScore.Value;
                    double? recencyWeight = p?.GetDouble("recencyWeight");
                    if (recencyWeight.HasValue) body["recencyWeight"] = recencyWeight.Value;
                    if (p?.GetString("superseded") != null) body["superseded"] = p.GetString("superseded");
                    long? linkExpansion = p?.GetInt64("linkExpansion");
                    if (linkExpansion.HasValue) body["linkExpansion"] = linkExpansion.Value;
                    double? diversity = p?.GetDouble("diversity");
                    if (diversity.HasValue) body["diversity"] = diversity.Value;
                    bool? rerank = p?.GetBoolean("rerank");
                    if (rerank.HasValue) body["rerank"] = rerank.Value;
                    double? minRerankScore = p?.GetDouble("minRerankScore");
                    if (minRerankScore.HasValue) body["minRerankScore"] = minRerankScore.Value;
                    List<string>? additionalQueries = StringList(p, "additionalQueries");
                    if (additionalQueries != null && additionalQueries.Count > 0) body["additionalQueries"] = additionalQueries;
                    bool? decompose = p?.GetBoolean("decompose");
                    if (decompose.HasValue) body["decompose"] = decompose.Value;
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories/search";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "memory_search", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "memory_delete",
                "Delete a memory by id. Required: tenantId, scopeId, memoryId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, memoryId = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "memoryId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/memories/" + Encode(Require(p, "memoryId")), null, "memory_delete", CurrentCredentials(), ct).ConfigureAwait(false));

            // ---- Scope read/update/delete ----

            _Server.RegisterTool(
                "scope_read",
                "Read a single scope by id. Required: tenantId, scopeId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" } }, required = new[] { "tenantId", "scopeId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")), null, "scope_read", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "scope_update",
                "Update a scope's name, description, or rerank settings (store provider and dimensionality are immutable); settings not passed are kept. Required: tenantId, scopeId. Optional: name, description, rerankEndpointId (empty string removes it), rerankCandidates, rerankMinScore.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        name = new { type = "string" },
                        description = new { type = "string" },
                        rerankEndpointId = new { type = "string", description = "Rerank endpoint id (rep_), or an empty string to stop reranking." },
                        rerankCandidates = new { type = "integer", description = "Candidates sent to the reranker (1..100)." },
                        rerankMinScore = new { type = "number", description = "Drop reranked hits scoring below this (0..1)." }
                    },
                    required = new[] { "tenantId", "scopeId" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    // The REST update replaces the whole scope, so start from the current scope and change only what was
                    // passed; sending just the name would clear the embedding endpoint and chunking settings.
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId"));
                    McpCallerCredentials credentials = CurrentCredentials();
                    Dictionary<string, object?> current = (Dictionary<string, object?>)await ProxyAsync(HttpMethod.Get, path, null, "scope_update", credentials, ct).ConfigureAwait(false);
                    if (!(current["success"] is bool ok && ok) || !(current.TryGetValue("data", out object? data) && data is JsonElement element && element.ValueKind == JsonValueKind.Object)) return current;

                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    foreach (JsonProperty property in element.EnumerateObject()) body[property.Name] = property.Value;
                    if (p?.GetString("name") != null) body["name"] = p.GetString("name");
                    if (p?.GetString("description") != null) body["description"] = p.GetString("description");
                    AddRerankSettings(p, body);
                    return await ProxyAsync(HttpMethod.Put, path, JsonSerializer.Serialize(body), "scope_update", credentials, ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "scope_delete",
                "Delete a scope and cascade its categories, memories, and scope instructions. Required: tenantId, scopeId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" } }, required = new[] { "tenantId", "scopeId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")), null, "scope_delete", CurrentCredentials(), ct).ConfigureAwait(false));

            // ---- Category read/update/delete ----

            _Server.RegisterTool(
                "category_read",
                "Read a single category by id. Required: tenantId, scopeId, categoryId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, categoryId = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "categoryId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/categories/" + Encode(Require(p, "categoryId")), null, "category_read", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "category_update",
                "Update a category. Required: tenantId, scopeId, categoryId, name. Optional: description, instructions.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, categoryId = new { type = "string" }, name = new { type = "string" }, description = new { type = "string" }, instructions = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "categoryId", "name" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    if (p?.GetString("description") != null) body["description"] = p.GetString("description");
                    if (p?.GetString("instructions") != null) body["instructions"] = p.GetString("instructions");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/categories/" + Encode(Require(p, "categoryId"));
                    return await ProxyAsync(HttpMethod.Put, path, JsonSerializer.Serialize(body), "category_update", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "category_delete",
                "Delete a category. Required: tenantId, scopeId, categoryId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string" }, categoryId = new { type = "string" } }, required = new[] { "tenantId", "scopeId", "categoryId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/categories/" + Encode(Require(p, "categoryId")), null, "category_delete", CurrentCredentials(), ct).ConfigureAwait(false));

            // ---- Model endpoint read/create/update/delete/health ----

            _Server.RegisterTool(
                "endpoint_read",
                "Read a single model endpoint by id. Required: tenantId, endpointId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, endpointId = new { type = "string" } }, required = new[] { "tenantId", "endpointId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoints/" + Encode(Require(p, "endpointId")), null, "endpoint_read", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "endpoint_create",
                "Create a model endpoint (embedding or inference). Required: tenantId, name, baseUrl. Optional: kind, apiFormat, authType + auth fields, model, dimensionality, healthCheckUrl, active. Requires tenant administration.",
                new { type = "object", properties = EndpointProperties(), required = new[] { "tenantId", "name", "baseUrl" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoints";
                    return await ProxyAsync(HttpMethod.Post, path, BuildEndpointBody(p), "endpoint_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "endpoint_update",
                "Update a model endpoint. Required: tenantId, endpointId, name, baseUrl. Optional: kind, apiFormat, authType + auth fields, model, dimensionality, healthCheckUrl, active. Requires tenant administration.",
                new { type = "object", properties = EndpointProperties(), required = new[] { "tenantId", "endpointId", "name", "baseUrl" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoints/" + Encode(Require(p, "endpointId"));
                    return await ProxyAsync(HttpMethod.Put, path, BuildEndpointBody(p), "endpoint_update", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "endpoint_delete",
                "Delete a model endpoint. Required: tenantId, endpointId. Requires tenant administration.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, endpointId = new { type = "string" } }, required = new[] { "tenantId", "endpointId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoints/" + Encode(Require(p, "endpointId")), null, "endpoint_delete", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "endpoint_health",
                "Probe and return the health of the tenant's model endpoints. Required: tenantId.",
                new { type = "object", properties = new { tenantId = new { type = "string" } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/endpoint-health", null, "endpoint_health", CurrentCredentials(), ct).ConfigureAwait(false));

            // ---- Chat with memory ----

            _Server.RegisterTool(
                "chat",
                "Ask a question answered from a scope's memory (retrieval-augmented). Required: tenantId, scopeId, question. Optional: topK (default 8), inferenceEndpointId, history. For a follow-up question, pass the earlier messages in history so the question is understood in context. Returns the answer plus cited memory ids.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string" },
                        scopeId = new { type = "string" },
                        question = new { type = "string" },
                        topK = new { type = "integer" },
                        inferenceEndpointId = new { type = "string" },
                        history = new
                        {
                            type = "array",
                            description = "Earlier messages in the conversation, oldest first. A follow-up question is rewritten into a standalone query for retrieval.",
                            items = new { type = "object", properties = new { role = new { type = "string", @enum = new[] { "user", "assistant" } }, content = new { type = "string" } }, required = new[] { "role", "content" } }
                        }
                    },
                    required = new[] { "tenantId", "scopeId", "question" }
                },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["question"] = Require(p, "question");
                    long? topK = p?.GetInt64("topK");
                    if (topK.HasValue) body["topK"] = topK.Value;
                    if (p?.GetString("inferenceEndpointId") != null) body["inferenceEndpointId"] = p.GetString("inferenceEndpointId");
                    List<Dictionary<string, string>>? history = ChatHistory(p);
                    if (history != null && history.Count > 0) body["history"] = history;
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/scopes/" + Encode(Require(p, "scopeId")) + "/chat";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "chat", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            // ---- RecallDB collections pass-through ----

            _Server.RegisterTool(
                "collection_enumerate",
                "List the RecallDB collections backing this tenant's scopes. Required: tenantId.",
                new { type = "object", properties = new { tenantId = new { type = "string" } }, required = new[] { "tenantId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/collections", null, "collection_enumerate", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "collection_read",
                "Read a single RecallDB collection by id. Required: tenantId, collectionId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, collectionId = new { type = "string" } }, required = new[] { "tenantId", "collectionId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/collections/" + Encode(Require(p, "collectionId")), null, "collection_read", CurrentCredentials(), ct).ConfigureAwait(false));

            _Server.RegisterTool(
                "collection_create",
                "Create a RecallDB collection directly. Required: tenantId, name, dimensionality. Optional: description. (Normally scopes provision their own collection.)",
                new { type = "object", properties = new { tenantId = new { type = "string" }, name = new { type = "string" }, dimensionality = new { type = "integer" }, description = new { type = "string" } }, required = new[] { "tenantId", "name", "dimensionality" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    long? dim = p?.GetInt64("dimensionality");
                    if (dim.HasValue) body["dimensionality"] = dim.Value;
                    if (p?.GetString("description") != null) body["description"] = p.GetString("description");
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/collections";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "collection_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "collection_delete",
                "Delete a RecallDB collection by id. Required: tenantId, collectionId.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, collectionId = new { type = "string" } }, required = new[] { "tenantId", "collectionId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/collections/" + Encode(Require(p, "collectionId")), null, "collection_delete", CurrentCredentials(), ct).ConfigureAwait(false));

            // ---- Instruction create/update/delete (tenant-global or scope-specific) ----

            _Server.RegisterTool(
                "instruction_create",
                "Create an instruction. Required: tenantId, name, content. Optional: scopeId (omit for a tenant-global instruction), mergeMode (Append|Replace|Hide, for scope instructions), position, active. Requires tenant administration.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, scopeId = new { type = "string", description = "Omit for tenant-global; set to attach to a scope." }, name = new { type = "string" }, content = new { type = "string" }, mergeMode = new { type = "string", description = "Append, Replace, or Hide (scope instructions)." }, position = new { type = "integer" }, active = new { type = "boolean" } }, required = new[] { "tenantId", "name", "content" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    body["content"] = Require(p, "content");
                    if (p?.GetString("mergeMode") != null) body["mergeMode"] = p.GetString("mergeMode");
                    long? position = p?.GetInt64("position");
                    if (position.HasValue) body["position"] = position.Value;
                    bool? active = p?.GetBoolean("active");
                    if (active.HasValue) body["active"] = active.Value;
                    string tenantId = Encode(Require(p, "tenantId"));
                    string? scopeId = p?.GetString("scopeId");
                    string path = string.IsNullOrEmpty(scopeId)
                        ? "/v1.0/api/tenants/" + tenantId + "/instructions"
                        : "/v1.0/api/tenants/" + tenantId + "/scopes/" + Encode(scopeId) + "/instructions";
                    return await ProxyAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), "instruction_create", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "instruction_update",
                "Update an instruction by id (tenant-global or scope-specific; the scope binding is preserved). Required: tenantId, instructionId, name, content. Optional: mergeMode, position, active. Requires tenant administration.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, instructionId = new { type = "string" }, name = new { type = "string" }, content = new { type = "string" }, mergeMode = new { type = "string" }, position = new { type = "integer" }, active = new { type = "boolean" } }, required = new[] { "tenantId", "instructionId", "name", "content" } },
                async (RpcParameters? p, CancellationToken ct) =>
                {
                    Dictionary<string, object?> body = new Dictionary<string, object?>();
                    body["name"] = Require(p, "name");
                    body["content"] = Require(p, "content");
                    if (p?.GetString("mergeMode") != null) body["mergeMode"] = p.GetString("mergeMode");
                    long? position = p?.GetInt64("position");
                    if (position.HasValue) body["position"] = position.Value;
                    bool? active = p?.GetBoolean("active");
                    if (active.HasValue) body["active"] = active.Value;
                    string path = "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/instructions/" + Encode(Require(p, "instructionId"));
                    return await ProxyAsync(HttpMethod.Put, path, JsonSerializer.Serialize(body), "instruction_update", CurrentCredentials(), ct).ConfigureAwait(false);
                });

            _Server.RegisterTool(
                "instruction_delete",
                "Delete an instruction by id. Required: tenantId, instructionId. Requires tenant administration.",
                new { type = "object", properties = new { tenantId = new { type = "string" }, instructionId = new { type = "string" } }, required = new[] { "tenantId", "instructionId" } },
                async (RpcParameters? p, CancellationToken ct) =>
                    await ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/" + Encode(Require(p, "tenantId")) + "/instructions/" + Encode(Require(p, "instructionId")), null, "instruction_delete", CurrentCredentials(), ct).ConfigureAwait(false));
        }

        #endregion
    }
}
