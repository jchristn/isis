namespace Test.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Minimal black-box REST client for the Isis API. It deliberately does not reference Isis assemblies, so the
    /// benchmark measures the API exactly as an external client sees it and can target any deployment.
    /// </summary>
    public class IsisClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The tenant resolved from the credential (set by <see cref="ConnectAsync"/>).
        /// </summary>
        public string TenantId { get; private set; } = string.Empty;

        /// <summary>
        /// The server base URL.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Times an upsert answered 503 or 429 is resent, with backoff from 2 to 60 seconds. Default 6.
        /// </summary>
        public int UpsertRetries { get; set; } = 6;

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="baseUrl">Isis REST base URL, for example http://127.0.0.1:18700.</param>
        /// <param name="accessKey">Credential access key.</param>
        public IsisClient(string baseUrl, string accessKey)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            BaseUrl = baseUrl.TrimEnd('/');
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 512,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _Http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromMinutes(10) };
            _Http.DefaultRequestHeaders.Add("x-access-key", accessKey);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the credential's tenant.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ConnectAsync(CancellationToken token)
        {
            JsonNode who = await GetJsonAsync("/v1.0/api/whoami", token).ConfigureAwait(false);
            TenantId = who["tenantId"]?.GetValue<string>() ?? throw new InvalidOperationException("whoami returned no tenantId.");
        }

        /// <summary>
        /// Find a model endpoint by name, creating it when absent.
        /// </summary>
        /// <param name="definition">Endpoint body (must include name and kind).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint id.</returns>
        public async Task<string> EnsureEndpointAsync(JsonObject definition, CancellationToken token)
        {
            string name = definition["name"]?.GetValue<string>() ?? throw new ArgumentException("Endpoint definition needs a name.");
            JsonNode list = await GetJsonAsync(TenantPath("/endpoints?maxResults=1000"), token).ConfigureAwait(false);
            foreach (JsonNode? item in Objects(list))
            {
                if (item != null && string.Equals(item["name"]?.GetValue<string>(), name, StringComparison.Ordinal))
                {
                    string id = item["id"]!.GetValue<string>();
                    await SendJsonAsync(HttpMethod.Put, TenantPath("/endpoints/" + id), definition, token).ConfigureAwait(false);
                    return id;
                }
            }

            JsonNode created = await SendJsonAsync(HttpMethod.Post, TenantPath("/endpoints"), definition, token).ConfigureAwait(false);
            return created["id"]!.GetValue<string>();
        }

        /// <summary>
        /// Find a scope id by name.
        /// </summary>
        /// <param name="name">Scope name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope id, or null.</returns>
        public async Task<string?> FindScopeAsync(string name, CancellationToken token)
        {
            JsonNode list = await GetJsonAsync(TenantPath("/scopes?maxResults=1000"), token).ConfigureAwait(false);
            foreach (JsonNode? item in Objects(list))
            {
                if (item != null && string.Equals(item["name"]?.GetValue<string>(), name, StringComparison.Ordinal)) return item["id"]!.GetValue<string>();
            }

            return null;
        }

        /// <summary>
        /// Create a scope.
        /// </summary>
        /// <param name="definition">Scope body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope id.</returns>
        public async Task<string> CreateScopeAsync(JsonObject definition, CancellationToken token)
        {
            JsonNode created = await SendJsonAsync(HttpMethod.Post, TenantPath("/scopes"), definition, token).ConfigureAwait(false);
            return created["id"]!.GetValue<string>();
        }

        /// <summary>
        /// Delete a scope (cascades to its store content).
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteScopeAsync(string scopeId, CancellationToken token)
        {
            using HttpResponseMessage response = await _Http.DeleteAsync(TenantPath("/scopes/" + scopeId), token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
                throw new InvalidOperationException("DELETE scope " + scopeId + " returned " + (int)response.StatusCode + ".");
        }

        /// <summary>
        /// List a scope's categories as name to id.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Category name to id (case-insensitive).</returns>
        public async Task<Dictionary<string, string>> ListCategoriesAsync(string scopeId, CancellationToken token)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            JsonNode list = await GetJsonAsync(TenantPath("/scopes/" + scopeId + "/categories?maxResults=1000"), token).ConfigureAwait(false);
            foreach (JsonNode? item in Objects(list))
            {
                if (item != null) map[item["name"]!.GetValue<string>()] = item["id"]!.GetValue<string>();
            }

            return map;
        }

        /// <summary>
        /// Create a category.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="name">Category name.</param>
        /// <param name="description">Category description.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The category id.</returns>
        public async Task<string> CreateCategoryAsync(string scopeId, string name, string description, CancellationToken token)
        {
            JsonObject body = new JsonObject { ["name"] = name, ["description"] = description };
            JsonNode created = await SendJsonAsync(HttpMethod.Post, TenantPath("/scopes/" + scopeId + "/categories"), body, token).ConfigureAwait(false);
            return created["id"]!.GetValue<string>();
        }

        /// <summary>
        /// Count the memories in a scope.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The total memory count.</returns>
        public async Task<long> CountMemoriesAsync(string scopeId, CancellationToken token)
        {
            return await CountMemoriesAsync(scopeId, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Count the memories in one category of a scope.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="categoryId">Category id, or null for the whole scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The memory count.</returns>
        public async Task<long> CountMemoriesAsync(string scopeId, string? categoryId, CancellationToken token)
        {
            string filter = string.IsNullOrEmpty(categoryId) ? string.Empty : "&category=" + Uri.EscapeDataString(categoryId);
            JsonNode list = await GetJsonAsync(TenantPath("/scopes/" + scopeId + "/memories?maxResults=1" + filter), token).ConfigureAwait(false);
            return list["totalRecords"]?.GetValue<long>() ?? 0;
        }

        /// <summary>
        /// Upsert a memory, timing the call.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="memory">Memory body (slug, categoryId, title, summary, body, ...).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The timed outcome, with any similar memories the server reported.</returns>
        public async Task<UpsertResponse> UpsertMemoryAsync(string scopeId, JsonObject memory, CancellationToken token)
        {
            // A 503 or 429 means a model endpoint behind Isis was at capacity after Isis's own retries; wait longer and
            // resend rather than recording a failed ingest. Elapsed time covers only the final attempt.
            TimedCall call = await TimedSendAsync(HttpMethod.Post, TenantPath("/scopes/" + scopeId + "/memories"), memory, token).ConfigureAwait(false);
            for (int attempt = 0; (call.StatusCode == 503 || call.StatusCode == 429) && attempt < UpsertRetries; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, attempt))), token).ConfigureAwait(false);
                call = await TimedSendAsync(HttpMethod.Post, TenantPath("/scopes/" + scopeId + "/memories"), memory, token).ConfigureAwait(false);
            }

            UpsertResponse response = new UpsertResponse { StatusCode = call.StatusCode, ElapsedMs = call.ElapsedMs, Error = call.IsSuccess ? null : Truncate(call.Body) };
            if (call.IsSuccess && call.Body.Contains("similarMemories", StringComparison.Ordinal))
            {
                JsonArray? similar = JsonNode.Parse(call.Body)?["similarMemories"] as JsonArray;
                foreach (JsonNode? item in similar ?? new JsonArray())
                {
                    string? slug = item?["slug"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(slug)) response.SimilarSlugs.Add(slug);
                }
            }

            return response;
        }

        /// <summary>
        /// Set a scope's rerank settings, keeping the rest of the scope as it is.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="rerankEndpointId">Rerank endpoint id, or null to stop reranking.</param>
        /// <param name="candidates">Candidates sent to the reranker.</param>
        /// <param name="minScore">Scope minimum rerank score, or null for none.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ConfigureRerankAsync(string scopeId, string? rerankEndpointId, int candidates, double? minScore, CancellationToken token)
        {
            JsonNode scope = await GetJsonAsync(TenantPath("/scopes/" + scopeId), token).ConfigureAwait(false);
            JsonObject body = scope.AsObject();
            body["rerankEndpointId"] = rerankEndpointId;
            body["rerankCandidates"] = candidates;
            body["rerankMinScore"] = minScore;
            await SendJsonAsync(HttpMethod.Put, TenantPath("/scopes/" + scopeId), body, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Search a scope, timing the call.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="queryText">Query text.</param>
        /// <param name="mode">Keyword, Semantic, or Hybrid.</param>
        /// <param name="topK">Result count.</param>
        /// <param name="categoryFilter">Optional category name or id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="options">Optional search settings (null members use the server default).</param>
        /// <returns>The parsed, timed response.</returns>
        public async Task<SearchResponse> SearchAsync(string scopeId, string queryText, string mode, int topK, string? categoryFilter, CancellationToken token, SearchOptions? options = null)
        {
            JsonObject body = new JsonObject { ["queryText"] = queryText, ["mode"] = mode, ["topK"] = topK };
            if (!string.IsNullOrEmpty(categoryFilter)) body["categoryFilter"] = categoryFilter;
            if (options != null)
            {
                if (options.RecencyWeight.HasValue) body["recencyWeight"] = options.RecencyWeight.Value;
                if (options.MinScore.HasValue) body["minScore"] = options.MinScore.Value;
                if (!string.IsNullOrEmpty(options.Superseded)) body["superseded"] = options.Superseded;
                if (options.LinkExpansion.HasValue) body["linkExpansion"] = options.LinkExpansion.Value;
                if (options.Diversity.HasValue) body["diversity"] = options.Diversity.Value;
                if (options.Rerank.HasValue) body["rerank"] = options.Rerank.Value;
                if (options.MinRerankScore.HasValue) body["minRerankScore"] = options.MinRerankScore.Value;
                if (options.TextWeight.HasValue) body["textWeight"] = options.TextWeight.Value;
                if (options.RrfK.HasValue) body["rrfK"] = options.RrfK.Value;
                if (options.Decompose) body["decompose"] = true;
                if (!string.IsNullOrEmpty(options.InferenceEndpointId)) body["inferenceEndpointId"] = options.InferenceEndpointId;
            }

            TimedCall call = await TimedSendAsync(HttpMethod.Post, TenantPath("/scopes/" + scopeId + "/memories/search"), body, token).ConfigureAwait(false);
            SearchResponse response = new SearchResponse { StatusCode = call.StatusCode, ElapsedMs = call.ElapsedMs };
            if (!call.IsSuccess)
            {
                response.Error = Truncate(call.Body);
                return response;
            }

            JsonNode? parsed = JsonNode.Parse(call.Body);
            response.EffectiveMode = parsed?["effectiveMode"]?.GetValue<string>() ?? string.Empty;
            response.Reranked = parsed?["reranked"]?.GetValue<bool>() ?? false;
            JsonArray? hits = parsed?["hits"] as JsonArray;
            if (hits != null)
            {
                foreach (JsonNode? hit in hits)
                {
                    if (hit == null) continue;
                    response.Hits.Add(new SearchHit
                    {
                        Slug = hit["slug"]?.GetValue<string>() ?? string.Empty,
                        Score = hit["score"]?.GetValue<double>() ?? 0.0,
                        VectorScore = hit["vectorScore"] != null ? hit["vectorScore"]!.GetValue<double>() : (double?)null
                    });
                }
            }

            return response;
        }

        /// <summary>
        /// Ask the chat-with-memory route a question, timing the call.
        /// </summary>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="question">The question.</param>
        /// <param name="topK">Memories to retrieve; 0 or less uses the server default.</param>
        /// <param name="inferenceEndpointId">Inference endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="history">Earlier messages sent with a follow-up question, oldest first, or null.</param>
        /// <returns>The parsed, timed response.</returns>
        public async Task<ChatResponse> ChatAsync(string scopeId, string question, int topK, string inferenceEndpointId, CancellationToken token, List<BenchmarkTurn>? history = null)
        {
            JsonObject body = new JsonObject { ["question"] = question, ["inferenceEndpointId"] = inferenceEndpointId };
            if (topK > 0) body["topK"] = topK;
            if (history != null && history.Count > 0)
            {
                JsonArray turns = new JsonArray();
                foreach (BenchmarkTurn turn in history) turns.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Content });
                body["history"] = turns;
            }
            TimedCall call = await TimedSendAsync(HttpMethod.Post, TenantPath("/scopes/" + scopeId + "/chat"), body, token).ConfigureAwait(false);
            ChatResponse response = new ChatResponse { StatusCode = call.StatusCode, ElapsedMs = call.ElapsedMs };
            if (!call.IsSuccess)
            {
                response.Error = Truncate(call.Body);
                return response;
            }

            JsonNode? parsed = JsonNode.Parse(call.Body);
            response.Answer = parsed?["answer"]?.GetValue<string>() ?? string.Empty;
            response.RetrievalMode = parsed?["retrievalMode"]?.ToString() ?? string.Empty;
            response.StandaloneQuestion = parsed?["standaloneQuestion"]?.GetValue<string>();
            response.TimeToFirstTokenMs = parsed?["timeToFirstTokenMs"]?.GetValue<double>() ?? 0.0;
            response.GenerationMs = parsed?["generationMs"]?.GetValue<double>() ?? 0.0;
            response.PromptTokens = parsed?["promptTokens"]?.GetValue<int>() ?? 0;
            response.CompletionTokens = parsed?["completionTokens"]?.GetValue<int>() ?? 0;
            JsonArray? citations = parsed?["citations"] as JsonArray;
            if (citations != null)
            {
                foreach (JsonNode? citation in citations)
                {
                    string? slug = citation?["slug"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(slug)) response.RetrievedSlugs.Add(slug);
                }
            }

            return response;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private string TenantPath(string suffix)
        {
            return "/v1.0/api/tenants/" + Uri.EscapeDataString(TenantId) + suffix;
        }

        private static IEnumerable<JsonNode?> Objects(JsonNode list)
        {
            JsonArray? objects = list["objects"] as JsonArray ?? list as JsonArray;
            return objects ?? new JsonArray();
        }

        private async Task<JsonNode> GetJsonAsync(string path, CancellationToken token)
        {
            using HttpResponseMessage response = await _Http.GetAsync(path, token).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("GET " + path + " returned " + (int)response.StatusCode + ": " + Truncate(text));
            return JsonNode.Parse(text) ?? new JsonObject();
        }

        private async Task<JsonNode> SendJsonAsync(HttpMethod method, string path, JsonNode body, CancellationToken token)
        {
            TimedCall call = await TimedSendAsync(method, path, body, token).ConfigureAwait(false);
            if (!call.IsSuccess) throw new InvalidOperationException(method + " " + path + " returned " + call.StatusCode + ": " + Truncate(call.Body));
            return JsonNode.Parse(call.Body) ?? new JsonObject();
        }

        private async Task<TimedCall> TimedSendAsync(HttpMethod method, string path, JsonNode body, CancellationToken token)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, path);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            long start = Stopwatch.GetTimestamp();
            try
            {
                using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                return new TimedCall
                {
                    StatusCode = (int)response.StatusCode,
                    IsSuccess = response.IsSuccessStatusCode,
                    Body = text,
                    ElapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds
                };
            }
            catch (HttpRequestException e)
            {
                return new TimedCall { StatusCode = 0, IsSuccess = false, Body = e.Message, ElapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds };
            }
            catch (TaskCanceledException e) when (!token.IsCancellationRequested)
            {
                return new TimedCall { StatusCode = 0, IsSuccess = false, Body = "timeout: " + e.Message, ElapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds };
            }
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > 400 ? text.Substring(0, 400) + "…" : text;
        }

        #endregion
    }
}
