namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.McpServer;
    using Isis.McpServer.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Automated Isis MCP server suite. Each case boots an in-process Isis REST server (via <see cref="ServerHarness"/>)
    /// and an <see cref="IsisMcpServer"/> bound to a free loopback port, then exercises the proxy pipeline and the raw
    /// MCP transport. The proxy returns a Dictionary&lt;string,object?&gt; envelope with keys success (bool),
    /// statusCode (int), tool (string), and data (a System.Text.Json.JsonElement).
    /// </summary>
    public static class McpSuite
    {
        #region Public-Methods

        /// <summary>
        /// Get the Isis MCP Touchstone test suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "mcp2",
                "Isis MCP Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Async("mcp2", "whoami", "whoami resolves the default tenant", WhoamiAsync),
                    TestCase.Async("mcp2", "scope-create", "scope_create creates a filesystem scope", ScopeCreateAsync),
                    TestCase.Async("mcp2", "category-create", "category_create creates a category", CategoryCreateAsync),
                    TestCase.Async("mcp2", "memory-upsert", "memory_upsert writes a memory", MemoryUpsertAsync),
                    TestCase.Async("mcp2", "memory-upsert-idempotent", "memory_upsert is idempotent by slug", MemoryUpsertIdempotentAsync),
                    TestCase.Async("mcp2", "memory-upsert-tolerant-type", "memory_upsert defaults an unknown 'type' instead of failing", MemoryUpsertTolerantTypeAsync),
                    TestCase.Async("mcp2", "memory-search", "memory_search returns hits", MemorySearchAsync),
                    TestCase.Async("mcp2", "memory-read", "memory_read reads a memory by id", MemoryReadAsync),
                    TestCase.Async("mcp2", "memory-enumerate", "memory_enumerate lists memories", MemoryEnumerateAsync),
                    TestCase.Async("mcp2", "category-enumerate", "category_enumerate lists categories", CategoryEnumerateAsync),
                    TestCase.Async("mcp2", "scope-enumerate", "scope_enumerate lists scopes", ScopeEnumerateAsync),
                    TestCase.Async("mcp2", "guide", "guide returns categories", GuideAsync),
                    TestCase.Async("mcp2", "memory-delete", "memory_delete removes a memory", MemoryDeleteAsync),
                    TestCase.Async("mcp2", "guide-not-found", "guide reports 404 for a missing scope", GuideNotFoundAsync),
                    TestCase.Async("mcp2", "anonymous-unauthorized", "anonymous credentials are rejected with 401", AnonymousUnauthorizedAsync),
                    TestCase.Async("mcp2", "access-key-only", "the access key alone (no secret) authorizes", AccessKeyOnlyAuthorizesAsync),
                    TestCase.Async("mcp2", "wrong-secret-rejected", "a present but wrong secret is rejected with 401", WrongSecretRejectedAsync),
                    TestCase.Async("mcp2", "bearer-access-key", "raw MCP initialize authenticates with a bearer access key", BearerAccessKeyHandshakeAsync),
                    TestCase.Async("mcp2", "mcp-handshake", "raw MCP initialize returns serverInfo", HandshakeAsync)
                });
        }

        #endregion

        #region Private-Methods-Cases

        private static async Task WhoamiAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            JsonElement who = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/whoami", null, "whoami", ctx.Access).ConfigureAwait(false), "whoami");
            if (who.GetProperty("tenantId").GetString() != "ten_default") throw new InvalidOperationException("Expected whoami to resolve tenant 'ten_default'.");
        }

        private static async Task ScopeCreateAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            if (!scopeId.StartsWith("scp_", StringComparison.Ordinal)) throw new InvalidOperationException("Expected a scp_ scope id, got '" + scopeId + "'.");
        }

        private static async Task CategoryCreateAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(categoryId)) throw new InvalidOperationException("Expected a category id.");
        }

        private static async Task MemoryUpsertAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            JsonElement memory = await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange; control the sleeve and collar.").ConfigureAwait(false);
            if (memory.GetProperty("slug").GetString() != "grip") throw new InvalidOperationException("Expected upserted slug 'grip'.");
            if (string.IsNullOrEmpty(memory.GetProperty("id").GetString())) throw new InvalidOperationException("Expected a memory id.");
        }

        private static async Task MemoryUpsertIdempotentAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);

            JsonElement first = await UpsertMemoryAsync(ctx, scopeId, categoryId, "layout", "V1", "first").ConfigureAwait(false);
            JsonElement second = await UpsertMemoryAsync(ctx, scopeId, categoryId, "layout", "V2", "second").ConfigureAwait(false);
            if (first.GetProperty("id").GetString() != second.GetProperty("id").GetString()) throw new InvalidOperationException("Upsert by slug must reuse the same id.");

            string path = "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories";
            JsonElement list = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, path, null, "memory_enumerate", ctx.Admin).ConfigureAwait(false), "enumerate");
            if (list.GetProperty("totalRecords").GetInt64() != 1) throw new InvalidOperationException("Expected exactly one memory after two upserts of the same slug.");
        }

        private static async Task MemoryUpsertTolerantTypeAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);

            // A less-capable agent may pass a 'type' that is not one of User/Feedback/Project/Reference (here
            // "General"). The write must still succeed — the unknown type defaults to Project — rather than
            // failing the whole body and reporting the misleading "requires a slug and a categoryId".
            string memoryBody = JsonSerializer.Serialize(new { categoryId, slug = "arch", title = "Arch", body = "overview", type = "General" });
            JsonElement saved = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories", memoryBody, "memory_upsert", ctx.Access).ConfigureAwait(false), "upsert unknown type");
            if (saved.GetProperty("slug").GetString() != "arch") throw new InvalidOperationException("Expected the memory to be created despite an unknown 'type'.");
            if (saved.GetProperty("type").GetString() != "Project") throw new InvalidOperationException("Expected an unknown 'type' to default to 'Project', got '" + saved.GetProperty("type").GetString() + "'.");
        }

        private static async Task MemorySearchAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange; control the sleeve and collar.").ConfigureAwait(false);

            string searchBody = JsonSerializer.Serialize(new { queryText = "grip collar", mode = "Keyword", topK = 5 });
            JsonElement search = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories/search", searchBody, "memory_search", ctx.Access).ConfigureAwait(false), "search");
            if (search.GetProperty("hits").GetArrayLength() < 1) throw new InvalidOperationException("Expected at least one search hit.");
        }

        private static async Task MemoryReadAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            JsonElement upserted = await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange.").ConfigureAwait(false);
            string memoryId = upserted.GetProperty("id").GetString() ?? throw new InvalidOperationException("No memory id.");

            JsonElement read = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories/" + memoryId, null, "memory_read", ctx.Access).ConfigureAwait(false), "read");
            if (read.GetProperty("id").GetString() != memoryId) throw new InvalidOperationException("Read returned the wrong memory id.");
            if (read.GetProperty("slug").GetString() != "grip") throw new InvalidOperationException("Read returned the wrong slug.");
        }

        private static async Task MemoryEnumerateAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange.").ConfigureAwait(false);

            JsonElement list = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories", null, "memory_enumerate", ctx.Access).ConfigureAwait(false), "enumerate");
            if (list.GetProperty("objects").GetArrayLength() < 1) throw new InvalidOperationException("Expected at least one enumerated memory.");
        }

        private static async Task CategoryEnumerateAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);

            JsonElement list = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/categories", null, "category_enumerate", ctx.Admin).ConfigureAwait(false), "category enumerate");
            if (list.GetProperty("objects").GetArrayLength() < 1) throw new InvalidOperationException("Expected at least one category.");
        }

        private static async Task ScopeEnumerateAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            await CreateScopeAsync(ctx).ConfigureAwait(false);

            JsonElement list = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes", null, "scope_enumerate", ctx.Admin).ConfigureAwait(false), "scope enumerate");
            if (list.GetProperty("objects").GetArrayLength() < 1) throw new InvalidOperationException("Expected at least one scope.");
        }

        private static async Task GuideAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);

            JsonElement guide = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/guide", null, "guide", ctx.Admin).ConfigureAwait(false), "guide");
            JsonElement categories = guide.GetProperty("categories");
            if (categories.GetArrayLength() < 1) throw new InvalidOperationException("Expected the guide to list at least one category.");
        }

        private static async Task MemoryDeleteAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            JsonElement upserted = await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange.").ConfigureAwait(false);
            string memoryId = upserted.GetProperty("id").GetString() ?? throw new InvalidOperationException("No memory id.");

            Envelope delete = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Delete, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories/" + memoryId, null, "memory_delete", ctx.Admin).ConfigureAwait(false));
            if (!delete.Success) throw new InvalidOperationException("Expected delete to succeed, got status " + delete.StatusCode + ".");

            Envelope read = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories/" + memoryId, null, "memory_read", ctx.Admin).ConfigureAwait(false));
            if (read.Success || read.StatusCode != 404) throw new InvalidOperationException("Expected the deleted memory to be gone (404), got status " + read.StatusCode + ".");
        }

        private static async Task GuideNotFoundAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            Envelope envelope = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/scp_missing/guide", null, "guide", ctx.Admin).ConfigureAwait(false));
            if (envelope.Success) throw new InvalidOperationException("Expected the guide for a missing scope to fail.");
            if (envelope.StatusCode != 404) throw new InvalidOperationException("Expected status 404 for a missing scope, got " + envelope.StatusCode + ".");
        }

        private static async Task AnonymousUnauthorizedAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            McpCallerCredentials anonymous = new McpCallerCredentials();
            Envelope envelope = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants", null, "scope_enumerate", anonymous).ConfigureAwait(false));
            if (envelope.Success) throw new InvalidOperationException("Expected an anonymous call to fail.");
            if (envelope.StatusCode != 401) throw new InvalidOperationException("Expected status 401 for anonymous credentials, got " + envelope.StatusCode + ".");
        }

        private static async Task AccessKeyOnlyAuthorizesAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            McpCallerCredentials accessOnly = new McpCallerCredentials { AccessKey = ctx.Harness.AccessKey };
            JsonElement who = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/whoami", null, "whoami", accessOnly).ConfigureAwait(false), "whoami");
            if (who.GetProperty("tenantId").GetString() != "ten_default") throw new InvalidOperationException("Expected access-key-only auth to resolve tenant 'ten_default'.");
        }

        private static async Task WrongSecretRejectedAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            McpCallerCredentials badSecret = new McpCallerCredentials { AccessKey = ctx.Harness.AccessKey, SecretKey = "not-the-secret" };
            Envelope envelope = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/whoami", null, "whoami", badSecret).ConfigureAwait(false));
            if (envelope.Success) throw new InvalidOperationException("Expected a present-but-wrong secret to be rejected.");
            if (envelope.StatusCode != 401) throw new InvalidOperationException("Expected status 401 for a wrong secret, got " + envelope.StatusCode + ".");
        }

        private static async Task BearerAccessKeyHandshakeAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Harness.AccessKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Expected HTTP 200 from a bearer-access-key initialize, got " + (int)response.StatusCode + " (" + text + ").");
            if (!text.Contains("serverInfo", StringComparison.Ordinal)) throw new InvalidOperationException("Expected the bearer initialize response to contain serverInfo: " + text);
        }

        private static async Task HandshakeAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Add("x-access-key", ctx.Harness.AccessKey);
            request.Headers.Add("x-secret-key", ctx.Harness.SecretKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Expected HTTP 200 from the MCP initialize handshake, got " + (int)response.StatusCode + " (" + text + ").");
            if (!text.Contains("serverInfo", StringComparison.Ordinal)) throw new InvalidOperationException("Expected the initialize response to contain serverInfo: " + text);
            if (!text.Contains("Isis.McpServer", StringComparison.Ordinal)) throw new InvalidOperationException("Expected the initialize response to name the Isis.McpServer: " + text);
        }

        #endregion

        #region Private-Methods-Setup

        private static async Task<string> CreateScopeAsync(McpContext ctx)
        {
            string target = Path.Combine(ctx.Harness.WorkDir, "mcpmem-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string scopeBody = JsonSerializer.Serialize(new { name = "mcpproj", storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = target });
            JsonElement scope = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/ten_default/scopes", scopeBody, "scope_create", ctx.Admin).ConfigureAwait(false), "create scope");
            return scope.GetProperty("id").GetString() ?? throw new InvalidOperationException("No scope id.");
        }

        private static async Task<string> CreateCategoryAsync(McpContext ctx, string scopeId)
        {
            string categoryBody = JsonSerializer.Serialize(new { name = "notes", instructions = "One idea per memory." });
            JsonElement category = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/categories", categoryBody, "category_create", ctx.Admin).ConfigureAwait(false), "create category");
            return category.GetProperty("id").GetString() ?? throw new InvalidOperationException("No category id.");
        }

        private static async Task<JsonElement> UpsertMemoryAsync(McpContext ctx, string scopeId, string categoryId, string slug, string title, string body)
        {
            string memoryBody = JsonSerializer.Serialize(new { categoryId, slug, title, body });
            return Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/ten_default/scopes/" + scopeId + "/memories", memoryBody, "memory_upsert", ctx.Access).ConfigureAwait(false), "upsert memory");
        }

        #endregion

        #region Private-Methods-Envelope

        private readonly struct Envelope
        {
            internal Envelope(bool success, int statusCode, JsonElement data)
            {
                Success = success;
                StatusCode = statusCode;
                Data = data;
            }

            internal bool Success { get; }

            internal int StatusCode { get; }

            internal JsonElement Data { get; }
        }

        private static Envelope Unpack(object envelopeObject)
        {
            if (envelopeObject is not Dictionary<string, object?> envelope) throw new InvalidOperationException("Unexpected envelope type.");
            bool success = envelope.TryGetValue("success", out object? successValue) && successValue is bool flag && flag;
            int statusCode = envelope.TryGetValue("statusCode", out object? sc) && sc is int i ? i : 0;
            JsonElement data = default;
            if (envelope.TryGetValue("data", out object? dataValue) && dataValue is JsonElement je) data = je;
            return new Envelope(success, statusCode, data);
        }

        private static JsonElement Data(object envelopeObject, string label)
        {
            Envelope envelope = Unpack(envelopeObject);
            if (!envelope.Success) throw new InvalidOperationException(label + ": proxy call failed (status " + envelope.StatusCode + ").");
            if (envelope.Data.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException(label + ": no data in envelope.");
            return envelope.Data;
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// Bundles a running REST harness and a running MCP server for the duration of a single test case.
        /// </summary>
        private sealed class McpContext : IDisposable
        {
            internal ServerHarness Harness { get; private set; } = null!;

            internal IsisMcpServer Mcp { get; private set; } = null!;

            internal int McpPort { get; private set; }

            internal McpCallerCredentials Admin { get; private set; } = null!;

            internal McpCallerCredentials Access { get; private set; } = null!;

            private CancellationTokenSource _Cts = null!;
            private bool _Disposed;

            internal static async Task<McpContext> StartAsync()
            {
                McpContext ctx = new McpContext();
                ctx.Harness = await ServerHarness.StartAsync().ConfigureAwait(false);

                McpServerSettings settings = new McpServerSettings
                {
                    Hostname = "127.0.0.1",
                    Port = GetFreePort(),
                    RestHostname = "127.0.0.1",
                    RestPort = ctx.Harness.Port
                };
                ctx.McpPort = settings.Port;

                ctx._Cts = new CancellationTokenSource();
                ctx.Mcp = new IsisMcpServer(settings);
                ctx.Mcp.Start(ctx._Cts.Token);
                await WaitForMcpAsync(settings.Port).ConfigureAwait(false);

                ctx.Admin = new McpCallerCredentials { AccessKey = ctx.Harness.AccessKey, SecretKey = ctx.Harness.SecretKey };
                ctx.Access = new McpCallerCredentials { AccessKey = ctx.Harness.AccessKey, SecretKey = ctx.Harness.SecretKey };
                return ctx;
            }

            public void Dispose()
            {
                if (_Disposed) return;
                _Disposed = true;

                try { _Cts?.Cancel(); } catch { }
                try { Mcp?.Dispose(); } catch { }
                try { _Cts?.Dispose(); } catch { }
                try { Harness?.Dispose(); } catch { }
            }

            private static int GetFreePort()
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }

            private static async Task WaitForMcpAsync(int port)
            {
                using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port) };
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    try
                    {
                        HttpResponseMessage response = await client.GetAsync("/").ConfigureAwait(false);
                        if ((int)response.StatusCode < 500) return;
                    }
                    catch (HttpRequestException)
                    {
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }

                throw new InvalidOperationException("MCP server did not become ready in time.");
            }
        }

        #endregion
    }
}
