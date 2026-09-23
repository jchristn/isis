namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Health;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.Server.Observability;
    using Isis.Server.Services;

    /// <summary>
    /// Service-layer Touchstone suite covering health checks, embedding/inference clients, and the
    /// memory and chat services in isolation (no HTTP server, using stub HTTP handlers).
    /// </summary>
    public static class ServiceSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the service test suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static Touchstone.Core.TestSuiteDescriptor Suite()
        {
            return new Touchstone.Core.TestSuiteDescriptor(
                "service",
                "Isis Service Suite",
                new System.Collections.Generic.List<Touchstone.Core.TestCaseDescriptor>
                {
                    // HealthCheckService.BuildKey
                    TestCase.Async("service", "buildkey-identical-equal", "BuildKey: identical endpoints share a key", BuildKeyIdenticalEqualAsync),
                    TestCase.Async("service", "buildkey-path-differs", "BuildKey: different health-check path differs", BuildKeyPathDiffersAsync),
                    TestCase.Async("service", "buildkey-method-differs", "BuildKey: GET vs HEAD differs", BuildKeyMethodDiffersAsync),
                    TestCase.Async("service", "buildkey-port-differs", "BuildKey: different port differs", BuildKeyPortDiffersAsync),
                    TestCase.Async("service", "buildkey-auth-differs", "BuildKey: auth vs no-auth differs", BuildKeyAuthDiffersAsync),
                    TestCase.Async("service", "buildkey-auth-type-differ", "BuildKey: different auth mechanisms differ", BuildKeyAuthTypeDiffersAsync),

                    // EndpointAuthenticator
                    TestCase.Sync("service", "auth-bearer", "Auth: bearer token sets Authorization header", AuthBearer),
                    TestCase.Sync("service", "auth-header", "Auth: api-key header uses the configured name", AuthApiKeyHeader),
                    TestCase.Sync("service", "auth-query", "Auth: query-param auth appends the configured parameter", AuthQueryParam),
                    TestCase.Sync("service", "auth-basic", "Auth: basic auth encodes username:password", AuthBasic),
                    TestCase.Sync("service", "auth-access-secret", "Auth: access/secret sets both configured headers", AuthAccessKeySecret),
                    TestCase.Sync("service", "auth-none", "Auth: None applies nothing", AuthNone),

                    // HealthCheckService probing
                    TestCase.Async("service", "probe-dedup-same-url", "Probe: shared URL is probed once", ProbeDedupSameUrlAsync),
                    TestCase.Async("service", "probe-distinct-urls", "Probe: distinct URLs are probed separately", ProbeDistinctUrlsAsync),
                    TestCase.Async("service", "probe-status-null-before", "Probe: status is null before any probe", ProbeStatusNullBeforeAsync),
                    TestCase.Async("service", "probe-healthy-after-threshold", "Probe: healthy after meeting threshold", ProbeHealthyAfterThresholdAsync),
                    TestCase.Async("service", "probe-not-healthy-before-threshold", "Probe: not healthy before threshold", ProbeNotHealthyBeforeThresholdAsync),
                    TestCase.Async("service", "probe-failure-unhealthy", "Probe: unexpected status becomes unhealthy", ProbeFailureUnhealthyAsync),
                    TestCase.Async("service", "probe-inactive-skipped", "Probe: inactive endpoints are skipped", ProbeInactiveSkippedAsync),
                    TestCase.Async("service", "snapshot-reflects-probed", "Snapshot reflects probed endpoints", SnapshotReflectsProbedAsync),
                    TestCase.Async("service", "probe-enriched-on-success", "Probe: success records first-check, last-healthy, latency, state change", ProbeEnrichedOnSuccessAsync),
                    TestCase.Async("service", "probe-enriched-on-failure", "Probe: failure records last-unhealthy and no last-healthy", ProbeEnrichedOnFailureAsync),
                    TestCase.Async("service", "probe-uptime-accumulates", "Probe: healthy interval accumulates uptime to 100%", ProbeUptimeAccumulatesAsync),
                    TestCase.Async("service", "probe-downtime-accumulates", "Probe: unhealthy interval accumulates downtime to 0% uptime", ProbeDowntimeAccumulatesAsync),

                    // EmbeddingService
                    TestCase.Async("service", "embed-openai-parse", "Embedding: OpenAI response parses", EmbedOpenAiParseAsync),
                    TestCase.Async("service", "embed-ollama-parse", "Embedding: Ollama response parses", EmbedOllamaParseAsync),
                    TestCase.Async("service", "embed-error-status", "Embedding: error status throws", EmbedErrorStatusAsync),
                    TestCase.Async("service", "embed-missing-data", "Embedding: missing data array throws", EmbedMissingDataAsync),

                    // InferenceService
                    TestCase.Async("service", "infer-openai", "Inference: OpenAI content parses", InferOpenAiAsync),
                    TestCase.Async("service", "infer-ollama", "Inference: Ollama content parses", InferOllamaAsync),
                    TestCase.Async("service", "infer-error-status", "Inference: error status throws", InferErrorStatusAsync),
                    TestCase.Async("service", "infer-missing-content", "Inference: missing content throws", InferMissingContentAsync),

                    // MemoryService
                    TestCase.Async("service", "memory-upsert-same-id", "Memory: re-upsert by slug reuses id", MemoryUpsertSameIdAsync),
                    TestCase.Async("service", "memory-upsert-version-increment", "Memory: re-upsert increments version", MemoryUpsertVersionIncrementAsync),
                    TestCase.Async("service", "memory-upsert-single-row", "Memory: re-upsert yields a single row", MemoryUpsertSingleRowAsync),
                    TestCase.Async("service", "memory-upsert-latest-wins", "Memory: latest content wins", MemoryUpsertLatestWinsAsync),
                    TestCase.Async("service", "memory-search-keyword-hit", "Memory: keyword search returns a hit", MemorySearchKeywordHitAsync),
                    TestCase.Async("service", "memory-delete-removes", "Memory: delete removes the row", MemoryDeleteRemovesAsync),
                    TestCase.Async("service", "tenant-delete-tears-down-store", "Tenant: nuke tears down the external tenant store and the row", TenantDeleteTearsDownStoreAsync),

                    // MemoryChatService
                    TestCase.Async("service", "chat-answer-with-citations", "Chat: grounded answer cites the memory", ChatAnswerWithCitationsAsync),
                    TestCase.Async("service", "chat-miss-lists-memories", "Chat: a search miss falls back to listing the scope's memories", ChatMissListsMemoriesAsync),
                    TestCase.Async("service", "chat-empty-scope-says-none", "Chat: an empty scope reports it has no memories", ChatEmptyScopeSaysNoneAsync),
                    TestCase.Async("service", "chat-filesystem-analyzes-all", "Chat: a filesystem scope is analyzed top-down, not keyword-searched", ChatFilesystemAnalyzesAllAsync),

                    // OperationClassifier
                    TestCase.Sync("service", "classify-scope-create", "Classify: POST /scopes is scope/create", ClassifyScopeCreate),
                    TestCase.Sync("service", "classify-scope-item", "Classify: GET /scopes/{id} is scope/read with ids", ClassifyScopeItem),
                    TestCase.Sync("service", "classify-memory-search", "Classify: POST memories/search is search/search", ClassifyMemorySearch),
                    TestCase.Sync("service", "classify-memory-delete", "Classify: DELETE memory carries scope + resource id", ClassifyMemoryDelete),
                    TestCase.Sync("service", "classify-batch-delete", "Classify: batch-delete maps to delete", ClassifyBatchDelete),
                    TestCase.Sync("service", "classify-chat", "Classify: chat and chat/stream are chat/chat", ClassifyChat),
                    TestCase.Sync("service", "classify-auth-token", "Classify: token login/logout by method", ClassifyAuthToken),
                    TestCase.Sync("service", "classify-ignores-meta", "Classify: health and observability meta endpoints are ignored", ClassifyIgnoresMeta)
                });
        }

        #endregion

        #region Private-Methods-OperationClassifier

        private static void ClassifyScopeCreate()
        {
            bool ok = OperationClassifier.TryClassify("POST", "/v1.0/api/tenants/ten_1/scopes", out string resourceType, out string operation, out string? resourceId, out string? scopeId);
            TestCase.Require(ok, "POST /scopes should classify.");
            TestCase.Require(resourceType == "scope" && operation == "create", "POST /scopes should be scope/create, got " + resourceType + "/" + operation + ".");
            TestCase.Require(resourceId == null && scopeId == null, "Collection-level create should carry no resource or scope id.");
        }

        private static void ClassifyScopeItem()
        {
            bool ok = OperationClassifier.TryClassify("GET", "/v1.0/api/tenants/ten_1/scopes/scp_9", out string resourceType, out string operation, out string? resourceId, out string? scopeId);
            TestCase.Require(ok, "GET /scopes/{id} should classify.");
            TestCase.Require(resourceType == "scope" && operation == "read", "GET /scopes/{id} should be scope/read.");
            TestCase.Require(resourceId == "scp_9" && scopeId == "scp_9", "Scope item route should set both resource and scope id to the scope.");
        }

        private static void ClassifyMemorySearch()
        {
            bool ok = OperationClassifier.TryClassify("POST", "/v1.0/api/tenants/ten_1/scopes/scp_9/memories/search", out string resourceType, out string operation, out string? resourceId, out string? scopeId);
            TestCase.Require(ok, "memories/search should classify.");
            TestCase.Require(resourceType == "search" && operation == "search", "memories/search should be search/search, got " + resourceType + "/" + operation + ".");
            TestCase.Require(scopeId == "scp_9", "Search should carry the scope id.");
        }

        private static void ClassifyMemoryDelete()
        {
            bool ok = OperationClassifier.TryClassify("DELETE", "/v1.0/api/tenants/ten_1/scopes/scp_9/memories/mem_5", out string resourceType, out string operation, out string? resourceId, out string? scopeId);
            TestCase.Require(ok, "DELETE memory should classify.");
            TestCase.Require(resourceType == "memory" && operation == "delete", "DELETE memory should be memory/delete.");
            TestCase.Require(resourceId == "mem_5" && scopeId == "scp_9", "Memory item delete should carry both memory id and scope id.");
        }

        private static void ClassifyBatchDelete()
        {
            bool ok = OperationClassifier.TryClassify("POST", "/v1.0/api/tenants/ten_1/scopes/scp_9/memories/batch-delete", out string resourceType, out string operation, out _, out _);
            TestCase.Require(ok, "batch-delete should classify.");
            TestCase.Require(resourceType == "memory" && operation == "delete", "memories/batch-delete should be memory/delete, got " + resourceType + "/" + operation + ".");
        }

        private static void ClassifyChat()
        {
            bool ok1 = OperationClassifier.TryClassify("POST", "/v1.0/api/tenants/ten_1/scopes/scp_9/chat", out string rt1, out string op1, out _, out string? sid1);
            TestCase.Require(ok1 && rt1 == "chat" && op1 == "chat", "chat should be chat/chat.");
            TestCase.Require(sid1 == "scp_9", "chat should carry the scope id.");

            bool ok2 = OperationClassifier.TryClassify("POST", "/v1.0/api/tenants/ten_1/scopes/scp_9/chat/stream", out string rt2, out string op2, out _, out _);
            TestCase.Require(ok2 && rt2 == "chat" && op2 == "chat", "chat/stream should be chat/chat.");
        }

        private static void ClassifyAuthToken()
        {
            bool login = OperationClassifier.TryClassify("POST", "/v1.0/api/token", out string rt1, out string op1, out _, out _);
            TestCase.Require(login && rt1 == "auth" && op1 == "login", "POST /token should be auth/login.");

            bool logout = OperationClassifier.TryClassify("DELETE", "/v1.0/api/token", out string rt2, out string op2, out _, out _);
            TestCase.Require(logout && rt2 == "auth" && op2 == "logout", "DELETE /token should be auth/logout.");
        }

        private static void ClassifyIgnoresMeta()
        {
            TestCase.Require(!OperationClassifier.TryClassify("GET", "/v1.0/api/health", out _, out _, out _, out _), "health should not classify.");
            TestCase.Require(!OperationClassifier.TryClassify("GET", "/v1.0/api/requests", out _, out _, out _, out _), "request-history listing should not classify.");
            TestCase.Require(!OperationClassifier.TryClassify("GET", "/v1.0/api/operations", out _, out _, out _, out _), "operations listing should not classify.");
            TestCase.Require(!OperationClassifier.TryClassify("GET", "/favicon.ico", out _, out _, out _, out _), "non-API paths should not classify.");
        }

        #endregion

        #region Private-Methods-BuildKey

        private static Task BuildKeyIdenticalEqualAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            TestCase.Require(HealthCheckService.BuildKey(a) == HealthCheckService.BuildKey(b), "Identical endpoints must share a dedup key.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyPathDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/other" };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "Different health-check paths must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyMethodDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.HEAD };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "GET and HEAD must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyPortDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9001", HealthCheckUrl = "/health" };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "Different ports must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyAuthDiffersAsync()
        {
            ModelEndpoint noAuth = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint withAuth = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckUseAuth = true, AuthType = EndpointAuthTypeEnum.BearerToken, AuthSecret = "secret-key" };
            TestCase.Require(HealthCheckService.BuildKey(noAuth) != HealthCheckService.BuildKey(withAuth), "Authenticated probe must differ from the anonymous one.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyAuthTypeDiffersAsync()
        {
            ModelEndpoint query = new ModelEndpoint { TenantId = "t", Name = "g", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckUseAuth = true, AuthType = EndpointAuthTypeEnum.QueryParam, AuthQueryParam = "key", AuthSecret = "k" };
            ModelEndpoint bearer = new ModelEndpoint { TenantId = "t", Name = "o", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckUseAuth = true, AuthType = EndpointAuthTypeEnum.BearerToken, AuthSecret = "k" };
            TestCase.Require(HealthCheckService.BuildKey(query) != HealthCheckService.BuildKey(bearer), "Different auth mechanisms must produce different dedup keys.");
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods-EndpointAuthenticator

        private static void AuthBearer()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.BearerToken, AuthSecret = "tok123" };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1/embeddings");
            EndpointAuthenticator.Apply(req, ep);
            TestCase.Require(req.Headers.Authorization != null && req.Headers.Authorization.Scheme == "Bearer" && req.Headers.Authorization.Parameter == "tok123", "Bearer auth must set 'Authorization: Bearer tok123'.");
        }

        private static void AuthApiKeyHeader()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.ApiKeyHeader, AuthHeaderName = "x-api-key", AuthSecret = "abc" };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1/embeddings");
            EndpointAuthenticator.Apply(req, ep);
            TestCase.Require(req.Headers.TryGetValues("x-api-key", out IEnumerable<string>? values) && values != null && string.Join(string.Empty, values) == "abc", "ApiKeyHeader auth must set the configured header to the secret.");
        }

        private static void AuthQueryParam()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.QueryParam, AuthQueryParam = "key", AuthSecret = "abc" };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1beta/models/x:generateContent");
            EndpointAuthenticator.Apply(req, ep);
            TestCase.Require(req.RequestUri != null && req.RequestUri.Query.Contains("key=abc", StringComparison.Ordinal), "QueryParam auth must append the configured parameter, got: " + req.RequestUri?.Query);
        }

        private static void AuthBasic()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.BasicAuth, AuthKeyId = "user", AuthSecret = "pass" };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1/embeddings");
            EndpointAuthenticator.Apply(req, ep);
            string expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:pass"));
            TestCase.Require(req.Headers.Authorization != null && req.Headers.Authorization.Scheme == "Basic" && req.Headers.Authorization.Parameter == expected, "Basic auth must encode base64(user:pass).");
        }

        private static void AuthAccessKeySecret()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.AccessKeySecret, AuthHeaderName = "x-access-key", AuthKeyId = "ak", AuthSecretHeaderName = "x-secret-key", AuthSecret = "sk" };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1/embeddings");
            EndpointAuthenticator.Apply(req, ep);
            TestCase.Require(req.Headers.TryGetValues("x-access-key", out IEnumerable<string>? ak) && ak != null && string.Join(string.Empty, ak) == "ak", "AccessKeySecret must set the access-key header.");
            TestCase.Require(req.Headers.TryGetValues("x-secret-key", out IEnumerable<string>? sk) && sk != null && string.Join(string.Empty, sk) == "sk", "AccessKeySecret must set the secret-key header.");
        }

        private static void AuthNone()
        {
            ModelEndpoint ep = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://h:9000", AuthType = EndpointAuthTypeEnum.None };
            using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "http://h:9000/v1/embeddings");
            EndpointAuthenticator.Apply(req, ep);
            TestCase.Require(req.Headers.Authorization == null, "None auth must not set an Authorization header.");
            TestCase.Require(req.RequestUri != null && string.IsNullOrEmpty(req.RequestUri.Query), "None auth must not add query parameters.");
        }

        #endregion

        #region Private-Methods-Probe

        private static async Task ProbeDedupSameUrlAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };

            int probes = await service.ProbeOnceAsync(new[] { a, b }).ConfigureAwait(false);
            TestCase.Require(probes == 1, "Two endpoints sharing a URL must be probed once, got " + probes + ".");
            TestCase.Require(handler.Count == 1, "The HTTP endpoint should be hit once, got " + handler.Count + ".");
        }

        private static async Task ProbeDistinctUrlsAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint c = new ModelEndpoint { TenantId = "t", Name = "c", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/other" };

            int first = await service.ProbeOnceAsync(new[] { a, b }).ConfigureAwait(false);
            TestCase.Require(first == 1, "First round with a shared URL must be probed once, got " + first + ".");
            TestCase.Require(handler.Count == 1, "First round should hit once, got " + handler.Count + ".");

            handler.Reset();
            int second = await service.ProbeOnceAsync(new[] { a, b, c }).ConfigureAwait(false);
            TestCase.Require(second == 2, "Adding a distinct path must yield two probes, got " + second + ".");
            TestCase.Require(handler.Count == 2, "Second round should hit twice, got " + handler.Count + ".");
        }

        private static Task ProbeStatusNullBeforeAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            TestCase.Require(service.GetStatus(a.Id) == null, "Status must be null before any probe.");
            return Task.CompletedTask;
        }

        private static async Task ProbeHealthyAfterThresholdAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 2 };

            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false);
            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(a.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(status!.IsHealthy, "Endpoint should be healthy after meeting the healthy threshold.");
        }

        private static async Task ProbeNotHealthyBeforeThresholdAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 2 };

            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(a.Id);
            TestCase.Require(status != null, "Status must exist after a single probe.");
            TestCase.Require(!status!.IsHealthy, "Endpoint must not be healthy before the healthy threshold is met.");
        }

        private static async Task ProbeEnrichedOnSuccessAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9100", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 1 };
            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(a.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(status!.IsHealthy, "Endpoint should be healthy at threshold 1.");
            TestCase.Require(status.FirstCheckUtc.HasValue, "FirstCheckUtc must be set on the first probe.");
            TestCase.Require(status.LastHealthyUtc.HasValue, "LastHealthyUtc must be set after a successful probe.");
            TestCase.Require(!status.LastUnhealthyUtc.HasValue, "LastUnhealthyUtc must remain null when no probe has failed.");
            TestCase.Require(status.LastStateChangeUtc.HasValue, "LastStateChangeUtc must be set when the endpoint becomes healthy.");
            TestCase.Require(status.LastLatencyMs >= 0, "LastLatencyMs must be recorded.");
            TestCase.Require(status.LastStatusCode == 200, "LastStatusCode must reflect the probe response, got " + status.LastStatusCode + ".");
        }

        private static async Task ProbeEnrichedOnFailureAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            // Expected status never matches the returned 200, so every probe fails.
            ModelEndpoint bad = new ModelEndpoint { TenantId = "t", Name = "bad", BaseUrl = "http://127.0.0.1:9101", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 599, UnhealthyThreshold = 1 };
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(bad.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(status!.LastUnhealthyUtc.HasValue, "LastUnhealthyUtc must be set after a failed probe.");
            TestCase.Require(!status.LastHealthyUtc.HasValue, "LastHealthyUtc must remain null when no probe has succeeded.");
            TestCase.Require(!string.IsNullOrEmpty(status.LastError), "A failed probe must record a LastError.");
        }

        private static async Task ProbeUptimeAccumulatesAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9102", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 1 };
            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false); // becomes healthy; no interval yet
            await Task.Delay(30).ConfigureAwait(false);
            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false); // healthy interval accrues to uptime

            EndpointHealthStatus? status = service.GetStatus(a.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(status!.TotalUptimeMs > 0, "A healthy interval must accumulate uptime, got " + status.TotalUptimeMs + "ms.");
            TestCase.Require(status.TotalDowntimeMs == 0, "No downtime should accrue while healthy, got " + status.TotalDowntimeMs + "ms.");
            TestCase.Require(status.UptimePercentage >= 99.9, "Uptime should be ~100% when always healthy, got " + status.UptimePercentage + "%.");
        }

        private static async Task ProbeDowntimeAccumulatesAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            // Expected status never matches, so the endpoint is never healthy and intervals accrue to downtime.
            ModelEndpoint bad = new ModelEndpoint { TenantId = "t", Name = "bad", BaseUrl = "http://127.0.0.1:9103", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 599, UnhealthyThreshold = 1 };
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);
            await Task.Delay(30).ConfigureAwait(false);
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(bad.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(status!.TotalDowntimeMs > 0, "An unhealthy interval must accumulate downtime, got " + status.TotalDowntimeMs + "ms.");
            TestCase.Require(status.UptimePercentage < 0.1, "Uptime should be ~0% when never healthy, got " + status.UptimePercentage + "%.");
        }

        private static async Task ProbeFailureUnhealthyAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint bad = new ModelEndpoint { TenantId = "t", Name = "bad", BaseUrl = "http://127.0.0.1:9001", HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 599 };

            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(bad.Id);
            TestCase.Require(status != null, "Status must exist after probing.");
            TestCase.Require(!status!.IsHealthy, "Endpoint should be unhealthy when the status never matches.");
            TestCase.Require(!string.IsNullOrEmpty(status.LastError), "A failed probe must record a LastError.");
        }

        private static async Task ProbeInactiveSkippedAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint inactive = new ModelEndpoint { TenantId = "t", Name = "off", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health", Active = false };

            int probes = await service.ProbeOnceAsync(new[] { inactive }).ConfigureAwait(false);
            TestCase.Require(probes == 0, "Inactive endpoints must not be probed, got " + probes + ".");
            TestCase.Require(handler.Count == 0, "No HTTP probe should be issued for an inactive endpoint, got " + handler.Count + ".");
            TestCase.Require(service.GetStatus(inactive.Id) == null, "An inactive endpoint should have no status.");
        }

        private static async Task SnapshotReflectsProbedAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/other" };
            ModelEndpoint inactive = new ModelEndpoint { TenantId = "t", Name = "off", BaseUrl = "http://127.0.0.1:9000", HealthCheckUrl = "/skip", Active = false };

            await service.ProbeOnceAsync(new[] { a, b, inactive }).ConfigureAwait(false);

            IReadOnlyList<EndpointHealthStatus> snapshot = service.Snapshot();
            TestCase.Require(snapshot.Count == 2, "Snapshot should reflect the two probed endpoints, got " + snapshot.Count + ".");
        }

        #endregion

        #region Private-Methods-Embedding

        private static async Task EmbedOpenAiParseAsync()
        {
            string json = JsonSerializer.Serialize(new { data = new[] { new { embedding = new[] { 0.1, 0.2, 0.3 } } } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9998" };

            float[] vector = await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false);
            TestCase.Require(vector.Length == 3, "Expected a length-3 vector, got " + vector.Length + ".");
            TestCase.Require(Math.Abs(vector[0] - 0.1f) < 0.0001f, "Vector element 0 was not parsed correctly.");
        }

        private static async Task EmbedOllamaParseAsync()
        {
            string json = JsonSerializer.Serialize(new { embedding = new[] { 0.1, 0.2 } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.Ollama, BaseUrl = "http://127.0.0.1:11434" };

            float[] vector = await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false);
            TestCase.Require(vector.Length == 2, "Expected a length-2 vector, got " + vector.Length + ".");
        }

        private static async Task EmbedErrorStatusAsync()
        {
            using HttpClient client = new HttpClient(new StubResponseHandler("{}", HttpStatusCode.InternalServerError));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9998" };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false),
                "An error status must throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static async Task EmbedMissingDataAsync()
        {
            using HttpClient client = new HttpClient(new StubResponseHandler("{\"nope\":1}"));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9998" };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false),
                "A response missing the data array must throw InvalidOperationException.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Inference

        private static async Task InferOpenAiAsync()
        {
            string json = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "hi" } } } });
            using StubResponseHandler handler = new StubResponseHandler(json);
            InferenceService service = new InferenceService(handler);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

            string content = await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false);
            TestCase.Require(content == "hi", "Expected the OpenAI content 'hi', got '" + content + "'.");
        }

        private static async Task InferOllamaAsync()
        {
            string json = JsonSerializer.Serialize(new { message = new { role = "assistant", content = "yo" } });
            using StubResponseHandler handler = new StubResponseHandler(json);
            InferenceService service = new InferenceService(handler);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.Ollama, BaseUrl = "http://127.0.0.1:11434" };

            string content = await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false);
            TestCase.Require(content == "yo", "Expected the Ollama content 'yo', got '" + content + "'.");
        }

        private static async Task InferErrorStatusAsync()
        {
            using StubResponseHandler handler = new StubResponseHandler("{}", HttpStatusCode.InternalServerError);
            InferenceService service = new InferenceService(handler);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false),
                "An error status must throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static async Task InferMissingContentAsync()
        {
            string json = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant" } } } });
            using StubResponseHandler handler = new StubResponseHandler(json);
            InferenceService service = new InferenceService(handler);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false),
                "A response missing content must throw InvalidOperationException.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Memory

        private static async Task MemoryUpsertSameIdAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                Memory first = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);
                Memory second = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V2", Body = "two" }).ConfigureAwait(false);
                TestCase.Require(first.Id == second.Id, "Re-upsert by slug must reuse the same id.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemoryUpsertVersionIncrementAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                Memory first = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);
                Memory second = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V2", Body = "two" }).ConfigureAwait(false);
                TestCase.Require(second.Version == first.Version + 1, "Version must increment on update.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemoryUpsertSingleRowAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);
                await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V2", Body = "two" }).ConfigureAwait(false);

                EnumerationResult<Memory> all = await t.Db.Memories.EnumerateAsync(scope.TenantId, scope.Id, null, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
                TestCase.Require(all.TotalRecords == 1, "Two upserts of the same slug must yield exactly one row, got " + all.TotalRecords + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemoryUpsertLatestWinsAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);
                Memory second = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V2", Body = "two" }).ConfigureAwait(false);
                TestCase.Require(second.Title == "V2" && second.Body == "two", "The latest content must win.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemorySearchKeywordHitAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);
                await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V2", Body = "two" }).ConfigureAwait(false);

                MemorySearchResult result = await service.SearchAsync(scope, new MemorySearchQuery { QueryText = "two", Mode = SearchModeEnum.Keyword }).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count >= 1, "Keyword search should return a hit for the memory body.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemoryDeleteRemovesAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService service) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                Memory memory = await service.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "V1", Body = "one" }).ConfigureAwait(false);

                bool deleted = await service.DeleteAsync(scope, memory).ConfigureAwait(false);
                TestCase.Require(deleted, "Delete should report success.");

                EnumerationResult<Memory> all = await t.Db.Memories.EnumerateAsync(scope.TenantId, scope.Id, null, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
                TestCase.Require(all.TotalRecords == 0, "Delete must remove the row, found " + all.TotalRecords + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task TenantDeleteTearsDownStoreAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                RecordingMemoryService memory = new RecordingMemoryService(t.Db);
                TenantLifecycleService lifecycle = new TenantLifecycleService(t.Db, memory);
                TenantProvisionResult provision = await lifecycle.ProvisionAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
                // A scope so the scope cascade runs ahead of the tenant-store teardown.
                await t.Db.Scopes.CreateAsync(new Scope { TenantId = provision.Tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = work }).ConfigureAwait(false);

                TenantDeleteOutcome outcome = await lifecycle.DeleteTenantAsync(provision.Tenant.Id).ConfigureAwait(false);

                TestCase.Require(outcome == TenantDeleteOutcome.Deleted, "Tenant delete should report Deleted, got " + outcome + ".");
                TestCase.Require(memory.TenantStoreTornDown, "Tenant nuke must issue the external tenant-store teardown.");
                TestCase.Require(memory.TornDownTenantId == provision.Tenant.Id, "Tenant-store teardown must target the deleted tenant.");
                Tenant? gone = await t.Db.Tenants.ReadAsync(provision.Tenant.Id).ConfigureAwait(false);
                TestCase.Require(gone == null, "The tenant row must be gone after a nuke.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        // A MemoryService that records the external tenant-store teardown so the tenant cascade can be observed
        // without a live RecallDB. Scope teardown still runs through the real base implementation.
        private sealed class RecordingMemoryService : MemoryService
        {
            public bool TenantStoreTornDown { get; private set; }

            public string? TornDownTenantId { get; private set; }

            public RecordingMemoryService(DatabaseDriverBase database) : base(database)
            {
            }

            public override Task DeleteTenantStoreAsync(string tenantId, CancellationToken token = default)
            {
                TenantStoreTornDown = true;
                TornDownTenantId = tenantId;
                return Task.CompletedTask;
            }
        }

        #endregion

        #region Private-Methods-Chat

        private static async Task ChatAnswerWithCitationsAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService memoryService) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "Centerline", Body = "Control the centerline; posture and framing win positions." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "Answer [a]" } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "How do posture and framing win positions?", 5).ConfigureAwait(false);

                TestCase.Require(answer.Answer.Contains("Answer", StringComparison.Ordinal), "Expected the synthesized answer text.");
                TestCase.Require(answer.Citations.Count >= 1, "Expected at least one citation.");
                TestCase.Require(answer.Citations[0].Slug == "a", "Expected a citation to the 'a' memory.");
                TestCase.Require(answer.RetrievalMode == SearchModeEnum.Keyword, "Filesystem retrieval must report keyword mode.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task ChatMissListsMemoriesAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService memoryService) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "Centerline", Body = "Control the centerline; posture and framing win positions." }).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "b", Title = "Grip", Body = "Win the grip to control the exchange." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "You have two memories: [a] and [b]." } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                // A broad meta-question whose words match no memory content — keyword search misses, so the
                // service must fall back to enumerating the scope's memories rather than sending "(none)".
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "What memories do you have?", 5).ConfigureAwait(false);

                TestCase.Require(answer.Citations.Count == 2, "A search miss with memories present must fall back to listing all of them, got " + answer.Citations.Count + ".");
                List<string?> slugs = answer.Citations.Select(c => c.Slug).ToList();
                TestCase.Require(slugs.Contains("a") && slugs.Contains("b"), "The fallback must include every scope memory.");
                TestCase.Require(!string.IsNullOrEmpty(answer.Notice), "The fallback must carry an explanatory notice.");
                TestCase.Require(!string.IsNullOrEmpty(answer.Answer), "Inference is still invoked, so an answer must be returned.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task ChatFilesystemAnalyzesAllAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService memoryService) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "Centerline", Body = "Control the centerline; posture and framing win positions." }).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "b", Title = "Escapes", Body = "Bridge and shrimp to recover guard from bottom." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "See [a]." } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                // The question lexically matches only "a" (posture). A keyword search would return just that
                // one; the top-down filesystem strategy must instead present BOTH memories for analysis.
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "How does posture help?", 5).ConfigureAwait(false);

                TestCase.Require(answer.Citations.Count == 2, "A filesystem scope must be analyzed top-down (all memories), not keyword-filtered; got " + answer.Citations.Count + " of 2.");
                List<string?> slugs = answer.Citations.Select(c => c.Slug).ToList();
                TestCase.Require(slugs.Contains("a") && slugs.Contains("b"), "Both memories must be presented, including the one that does not match the question's keywords.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task ChatEmptyScopeSaysNoneAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService memoryService) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                _ = category; // no memories are written — the scope is intentionally empty.

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "This scope has no memories yet." } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "What memories do you have?", 5).ConfigureAwait(false);

                TestCase.Require(answer.Citations.Count == 0, "An empty scope must yield no citations.");
                TestCase.Require(!string.IsNullOrEmpty(answer.Notice), "An empty scope must carry a notice explaining there are no memories.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        #endregion

        #region Private-Methods-Helpers

        private static async Task<(Scope Scope, Category Category, MemoryService Service)> SetupMemoryAsync(DatabaseDriverBase db, string work)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = work }).ConfigureAwait(false);
            Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);
            MemoryService service = new MemoryService(db);
            return (scope, category, service);
        }

        private static string NewWork()
        {
            return Path.Combine(Path.GetTempPath(), "isis-svc-" + Guid.NewGuid().ToString("N"));
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        #endregion
    }
}
