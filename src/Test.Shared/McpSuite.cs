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
                    TestCase.Async("mcp2", "memory-search-min-score", "memory_search forwards minScore (an unreachable threshold returns no hits)", MemorySearchMinScoreAsync),
                    TestCase.Async("mcp2", "memory-upsert-supersedes", "memory_upsert forwards supersedes and memory_search marks the replaced hit", MemoryUpsertSupersedesAsync),
                    TestCase.Async("mcp2", "scope-update-keeps-settings", "scope_update changes only the fields passed and keeps the rest", ScopeUpdateKeepsSettingsAsync),
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
                    TestCase.Async("mcp2", "mcp-handshake", "raw MCP initialize returns serverInfo", HandshakeAsync),
                    TestCase.Async("mcp2", "tools-parity", "tools/list exposes exactly the REST-parity tool set and nothing else", ToolsParityAsync),
                    TestCase.Async("mcp2", "tools-list-stateless-exact", "stateless tools/list carries only the Isis tools (no Voltaic demo tools)", StatelessToolsListExactAsync),
                    TestCase.Async("mcp2", "ping-handshake-empty", "protocol ping returns an empty result, not \"pong\"", PingHandshakeEmptyAsync),
                    TestCase.Async("mcp2", "ping-stateless-complete", "stateless ping returns only resultType complete", PingStatelessCompleteAsync),
                    TestCase.Async("mcp2", "ping-unauthenticated", "protocol ping succeeds without credentials", PingUnauthenticatedAsync),
                    TestCase.Async("mcp2", "removed-tools-rejected", "tools/call to the removed Voltaic demo tools (ping, echo, getTime, getSessions) fails", RemovedToolsRejectedAsync),
                    TestCase.Async("mcp2", "bare-tool-method-rejected", "calling a tool as a bare JSON-RPC method returns -32601", BareToolMethodRejectedAsync),
                    TestCase.Async("mcp2", "tools-call-unauthorized", "tools/call without credentials is rejected with 401", ToolsCallUnauthorizedAsync),
                    TestCase.Async("mcp2", "endpoint-schema-declares-ids", "every tool schema declares each argument it requires", EndpointSchemaDeclaresIdsAsync),
                    TestCase.Async("mcp2", "stateless-claude-sequence", "The Claude Code 2.1.x stateless 2026-07-28 discover, list, and call sequence works with a bearer access key", StatelessClaudeSequenceAsync),
                    TestCase.Async("mcp2", "stateless-unauthorized", "A stateless 2026-07-28 request without credentials is rejected with 401", StatelessUnauthorizedAsync),
                    TestCase.Async("mcp2", "initialize-caps-stateless-version", "initialize requesting 2026-07-28 negotiates the newest handshake revision", InitializeCapsStatelessVersionAsync),
                    TestCase.Async("mcp2", "endpoint-crud", "endpoint_create/read/update/delete proxy round-trips", EndpointCrudAsync),
                    TestCase.Async("mcp2", "scope-update-delete", "scope_update and scope_delete proxy round-trips", ScopeUpdateDeleteAsync)
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

        private static async Task MemorySearchMinScoreAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            await UpsertMemoryAsync(ctx, scopeId, categoryId, "grip", "Grip fighting", "Win the grip to win the exchange; control the sleeve and collar.").ConfigureAwait(false);

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            Dictionary<string, object?> arguments = new Dictionary<string, object?>
            {
                { "tenantId", "ten_default" },
                { "scopeId", scopeId },
                { "queryText", "grip collar" },
                { "mode", "Keyword" },
                { "minScore", 1000000.0 }
            };
            Dictionary<string, object?> callParams = new Dictionary<string, object?> { { "name", "memory_search" }, { "arguments", arguments } };
            using JsonDocument call = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/call", 1, callParams, "memory_search", HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            // The tool result's text content is the proxy envelope as JSON: { success, statusCode, tool, data }.
            string envelopeText = call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
            using JsonDocument envelope = JsonDocument.Parse(envelopeText);
            int hits = envelope.RootElement.GetProperty("data").GetProperty("hits").GetArrayLength();
            if (hits != 0) throw new InvalidOperationException("An unreachable minScore should return no hits, got " + hits + ": " + envelopeText);
        }

        private static async Task<JsonElement> CallToolAsync(McpContext ctx, HttpClient client, string tool, Dictionary<string, object?> arguments)
        {
            Dictionary<string, object?> callParams = new Dictionary<string, object?> { { "name", tool }, { "arguments", arguments } };
            using JsonDocument call = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/call", 1, callParams, tool, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            string envelopeText = call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
            using JsonDocument envelope = JsonDocument.Parse(envelopeText);
            if (!envelope.RootElement.GetProperty("success").GetBoolean()) throw new InvalidOperationException(tool + " failed: " + envelopeText);
            return envelope.RootElement.GetProperty("data").Clone();
        }

        private static async Task MemoryUpsertSupersedesAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(ctx, scopeId).ConfigureAwait(false);
            await UpsertMemoryAsync(ctx, scopeId, categoryId, "ttl-old", "Cache TTL", "The quote cache TTL is 15 minutes; quote cache quote cache.").ConfigureAwait(false);

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            JsonElement saved = await CallToolAsync(ctx, client, "memory_upsert", new Dictionary<string, object?>
            {
                { "tenantId", "ten_default" },
                { "scopeId", scopeId },
                { "categoryId", categoryId },
                { "slug", "ttl-new" },
                { "body", "The quote cache TTL is 5 minutes with jitter." },
                { "supersedes", new[] { "ttl-old" } }
            }).ConfigureAwait(false);
            string supersedes = saved.GetProperty("supersedes").ToString();
            if (!supersedes.Contains("ttl-old", StringComparison.Ordinal)) throw new InvalidOperationException("memory_upsert should forward supersedes, got " + saved);

            JsonElement search = await CallToolAsync(ctx, client, "memory_search", new Dictionary<string, object?>
            {
                { "tenantId", "ten_default" },
                { "scopeId", scopeId },
                { "queryText", "quote cache" },
                { "mode", "Keyword" },
                { "superseded", "Demote" }
            }).ConfigureAwait(false);
            JsonElement hits = search.GetProperty("hits");
            if (hits.GetArrayLength() < 2 || hits[0].GetProperty("slug").GetString() != "ttl-new") throw new InvalidOperationException("The replacement should rank first: " + search);
            if (hits[1].GetProperty("supersededBy").GetString() != "ttl-new") throw new InvalidOperationException("The replaced hit should carry supersededBy: " + search);
        }

        private static async Task ScopeUpdateKeepsSettingsAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            JsonElement before = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/tenants/ten_default/scopes/" + scopeId, null, "scope_read", ctx.Admin).ConfigureAwait(false), "read scope");
            string targetPath = before.GetProperty("targetPath").GetString() ?? string.Empty;

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            JsonElement updated = await CallToolAsync(ctx, client, "scope_update", new Dictionary<string, object?>
            {
                { "tenantId", "ten_default" },
                { "scopeId", scopeId },
                { "description", "updated through MCP" },
                { "rerankCandidates", 40 }
            }).ConfigureAwait(false);

            if (updated.GetProperty("description").GetString() != "updated through MCP") throw new InvalidOperationException("scope_update should set the description: " + updated);
            if (updated.GetProperty("rerankCandidates").GetInt32() != 40) throw new InvalidOperationException("scope_update should set rerankCandidates: " + updated);
            if (updated.GetProperty("name").GetString() != before.GetProperty("name").GetString()) throw new InvalidOperationException("scope_update should keep the name: " + updated);
            if (updated.GetProperty("targetPath").GetString() != targetPath || string.IsNullOrEmpty(targetPath)) throw new InvalidOperationException("scope_update should keep the target path: " + updated);
        }

        private static async Task StatelessClaudeSequenceAsync()
        {
            // Regression for the "Claude Code sees no Isis tools" bug (Voltaic before 1.1.0): Claude Code 2.1.x never
            // sends initialize. It opens with server/discover, picks the stateless 2026-07-28 revision, and rejects any
            // result missing resultType, or a list result missing ttlMs/cacheScope.
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            using JsonDocument discover = await SendStatelessAsync(client, ctx.Harness.AccessKey, "server/discover", "discover-1", null, null, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            JsonElement discoverResult = discover.RootElement.GetProperty("result");
            RequireResultType(discoverResult, "server/discover");
            bool offersStateless = false;
            foreach (JsonElement version in discoverResult.GetProperty("supportedVersions").EnumerateArray())
            {
                if (version.GetString() == "2026-07-28") offersStateless = true;
            }

            if (!offersStateless) throw new InvalidOperationException("server/discover should offer the stateless 2026-07-28 revision: " + discoverResult.GetRawText());

            using JsonDocument tools = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/list", 2, null, null, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            JsonElement toolsResult = tools.RootElement.GetProperty("result");
            RequireResultType(toolsResult, "tools/list");
            if (!toolsResult.TryGetProperty("ttlMs", out JsonElement ttl) || ttl.ValueKind != JsonValueKind.Number) throw new InvalidOperationException("tools/list must carry a numeric ttlMs under 2026-07-28: " + toolsResult.GetRawText());
            if (!toolsResult.TryGetProperty("cacheScope", out JsonElement scope) || string.IsNullOrEmpty(scope.GetString())) throw new InvalidOperationException("tools/list must carry a cacheScope under 2026-07-28: " + toolsResult.GetRawText());

            bool hasSearch = false;
            foreach (JsonElement tool in toolsResult.GetProperty("tools").EnumerateArray())
            {
                if (tool.GetProperty("name").GetString() == "memory_search") hasSearch = true;
            }

            if (!hasSearch) throw new InvalidOperationException("tools/list should return the Isis tools (memory_search missing).");

            Dictionary<string, object?> callParams = new Dictionary<string, object?> { { "name", "whoami" }, { "arguments", new Dictionary<string, object?>() } };
            using JsonDocument call = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/call", 3, callParams, "whoami", HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            JsonElement callResult = call.RootElement.GetProperty("result");
            RequireResultType(callResult, "tools/call");
            if (!callResult.GetRawText().Contains("ten_default", StringComparison.Ordinal)) throw new InvalidOperationException("tools/call whoami should reach REST as the authenticated caller and return tenant ten_default: " + callResult.GetRawText());
        }

        private static async Task StatelessUnauthorizedAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            using JsonDocument? rejected = await SendStatelessAsync(client, null, "tools/list", 1, null, null, HttpStatusCode.Unauthorized).ConfigureAwait(false);
        }

        private static async Task InitializeCapsStatelessVersionAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Harness.AccessKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2026-07-28\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}", Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("initialize returned " + (int)response.StatusCode + ": " + text);

            using JsonDocument doc = JsonDocument.Parse(text);
            string? negotiated = doc.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString();
            if (negotiated != "2025-11-25") throw new InvalidOperationException("initialize must not agree to the stateless 2026-07-28 revision; expected 2025-11-25, got " + negotiated + ".");
        }

        private static async Task<JsonDocument?> SendStatelessAsync(HttpClient client, string? accessKey, string method, object id, Dictionary<string, object?>? parameters, string? nameHeader, HttpStatusCode expected)
        {
            // Mirrors the request shape Claude Code 2.1.x sends on the stateless revision: protocol-version and
            // Mcp-Method headers (plus Mcp-Name for tools/call), no session id, and the client identity in _meta.
            Dictionary<string, object?> withMeta = parameters != null ? new Dictionary<string, object?>(parameters) : new Dictionary<string, object?>();
            withMeta["_meta"] = new Dictionary<string, object?>
            {
                { "io.modelcontextprotocol/protocolVersion", "2026-07-28" },
                { "io.modelcontextprotocol/clientInfo", new Dictionary<string, object?> { { "name", "claude-code" }, { "version", "2.1.281" } } },
                { "io.modelcontextprotocol/clientCapabilities", new Dictionary<string, object?>() }
            };

            Dictionary<string, object?> body = new Dictionary<string, object?> { { "jsonrpc", "2.0" }, { "id", id }, { "method", method }, { "params", withMeta } };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            if (accessKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            if (nameHeader != null) request.Headers.Add("Mcp-Name", nameHeader);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != expected) throw new InvalidOperationException("Stateless " + method + " expected HTTP " + (int)expected + " but got " + (int)response.StatusCode + ": " + text);
            if (expected != HttpStatusCode.OK) return null;

            JsonDocument parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.TryGetProperty("error", out JsonElement error)) throw new InvalidOperationException("Stateless " + method + " returned a JSON-RPC error: " + error.GetRawText());
            return parsed;
        }

        private static void RequireResultType(JsonElement result, string method)
        {
            if (!result.TryGetProperty("resultType", out JsonElement resultType) || resultType.GetString() != "complete")
                throw new InvalidOperationException(method + " must carry resultType \"complete\" under 2026-07-28: " + result.GetRawText());
        }

        private static async Task ToolsParityAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);

            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            // Initialize first (capture the session id if the transport issues one), then list tools.
            using HttpRequestMessage init = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Harness.AccessKey);
            init.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            init.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            init.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}", Encoding.UTF8, "application/json");
            HttpResponseMessage initResp = await client.SendAsync(init).ConfigureAwait(false);
            string? session = initResp.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? ids) ? System.Linq.Enumerable.FirstOrDefault(ids) : null;

            using HttpRequestMessage list = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.Harness.AccessKey);
            list.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            list.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!string.IsNullOrEmpty(session)) list.Headers.Add("Mcp-Session-Id", session);
            list.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}", Encoding.UTF8, "application/json");
            HttpResponseMessage listResp = await client.SendAsync(list).ConfigureAwait(false);
            string text = await listResp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (listResp.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("tools/list returned " + (int)listResp.StatusCode + ": " + text);

            using JsonDocument doc = JsonDocument.Parse(text);
            RequireExactTools(doc.RootElement.GetProperty("result"), "tools/list");
        }

        private static async Task StatelessToolsListExactAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            using JsonDocument tools = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/list", 1, null, null, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            RequireExactTools(tools.RootElement.GetProperty("result"), "stateless tools/list");
        }

        private static async Task PingHandshakeEmptyAsync()
        {
            // Voltaic 2.x answers the protocol ping itself with {} as the MCP specification requires; 1.x returned "pong".
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            string? session = await InitializeAsync(client, ctx.Harness.AccessKey).ConfigureAwait(false);
            RawResponse ping = await SendRawAsync(client, ctx.Harness.AccessKey, session, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}").ConfigureAwait(false);
            if (ping.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("ping returned " + (int)ping.StatusCode + ": " + ping.Text);

            using JsonDocument doc = JsonDocument.Parse(ping.Text);
            JsonElement result = doc.RootElement.GetProperty("result");
            if (result.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("ping must return an object result, not " + result.ValueKind + ": " + ping.Text);
            foreach (JsonProperty property in result.EnumerateObject()) throw new InvalidOperationException("ping must return an empty object under a handshake revision: " + ping.Text);
        }

        private static async Task PingStatelessCompleteAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            using JsonDocument ping = await SendStatelessAsync(client, ctx.Harness.AccessKey, "ping", 1, null, null, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            JsonElement result = ping.RootElement.GetProperty("result");
            if (result.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("stateless ping must return an object result: " + ping.RootElement.GetRawText());
            RequireResultType(result, "ping");
            foreach (JsonProperty property in result.EnumerateObject())
            {
                if (property.Name != "resultType") throw new InvalidOperationException("stateless ping must carry only resultType: " + result.GetRawText());
            }
        }

        private static async Task PingUnauthenticatedAsync()
        {
            // The ping bypass reaches only Voltaic's protocol handler, which runs no Isis code, so it needs no credential.
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            RawResponse ping = await SendRawAsync(client, null, null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}").ConfigureAwait(false);
            if (ping.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("An unauthenticated ping should succeed, got " + (int)ping.StatusCode + ": " + ping.Text);
            if (ping.Text.Contains("pong", StringComparison.Ordinal)) throw new InvalidOperationException("ping must no longer return \"pong\": " + ping.Text);
            if (ping.Text.Contains("ten_default", StringComparison.Ordinal)) throw new InvalidOperationException("An unauthenticated ping must not reach Isis: " + ping.Text);
        }

        private static async Task RemovedToolsRejectedAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            string? session = await InitializeAsync(client, ctx.Harness.AccessKey).ConfigureAwait(false);

            int id = 10;
            foreach (string tool in new[] { "ping", "echo", "getTime", "getSessions", "getClients" })
            {
                string body = "{\"jsonrpc\":\"2.0\",\"id\":" + id++ + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":{}}}";
                RawResponse response = await SendRawAsync(client, ctx.Harness.AccessKey, session, body).ConfigureAwait(false);
                if (!IsFailure(response)) throw new InvalidOperationException("tools/call " + tool + " should fail now that Voltaic no longer publishes it: " + response.Text);
                if (session != null && response.Text.Contains(session, StringComparison.Ordinal)) throw new InvalidOperationException("tools/call " + tool + " must not disclose session ids: " + response.Text);
            }
        }

        private static async Task BareToolMethodRejectedAsync()
        {
            // Voltaic 1.x also registered each tool as a bare JSON-RPC method, which skipped tools/call and its schema
            // validation. In 2.x only tools/call reaches a tool.
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };
            string? session = await InitializeAsync(client, ctx.Harness.AccessKey).ConfigureAwait(false);

            RawResponse bare = await SendRawAsync(client, ctx.Harness.AccessKey, session, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"whoami\",\"params\":{}}").ConfigureAwait(false);
            if (bare.Text.Contains("ten_default", StringComparison.Ordinal)) throw new InvalidOperationException("A bare whoami call must not reach the tool: " + bare.Text);
            if (!bare.Text.Contains("-32601", StringComparison.Ordinal)) throw new InvalidOperationException("A bare whoami call should return -32601 (method not found), got " + (int)bare.StatusCode + ": " + bare.Text);

            RawResponse viaTools = await SendRawAsync(client, ctx.Harness.AccessKey, session, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}").ConfigureAwait(false);
            if (viaTools.StatusCode != HttpStatusCode.OK || !viaTools.Text.Contains("ten_default", StringComparison.Ordinal)) throw new InvalidOperationException("whoami through tools/call should succeed: " + viaTools.Text);
        }

        private static async Task ToolsCallUnauthorizedAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            RawResponse call = await SendRawAsync(client, null, null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"whoami\",\"arguments\":{}}}").ConfigureAwait(false);
            if (call.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidOperationException("tools/call without credentials should be rejected with 401, got " + (int)call.StatusCode + ": " + call.Text);

            Dictionary<string, object?> callParams = new Dictionary<string, object?> { { "name", "whoami" }, { "arguments", new Dictionary<string, object?>() } };
            using JsonDocument? rejected = await SendStatelessAsync(client, null, "tools/call", 2, callParams, "whoami", HttpStatusCode.Unauthorized).ConfigureAwait(false);
        }

        private static async Task EndpointSchemaDeclaresIdsAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + ctx.McpPort) };

            using JsonDocument tools = await SendStatelessAsync(client, ctx.Harness.AccessKey, "tools/list", 1, null, null, HttpStatusCode.OK).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Stateless request returned no body.");
            foreach (JsonElement tool in tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray())
            {
                string name = tool.GetProperty("name").GetString() ?? string.Empty;
                JsonElement schema = tool.GetProperty("inputSchema");
                if (!schema.TryGetProperty("required", out JsonElement required)) continue;
                JsonElement properties = schema.GetProperty("properties");
                foreach (JsonElement field in required.EnumerateArray())
                {
                    if (!properties.TryGetProperty(field.GetString() ?? string.Empty, out _)) throw new InvalidOperationException(name + " requires '" + field.GetString() + "' but its schema does not declare it: " + schema.GetRawText());
                }
            }
        }

        private static readonly string[] _ExpectedTools = new[]
        {
            "whoami", "instructions", "guide",
            "scope_enumerate", "scope_create", "scope_read", "scope_update", "scope_delete",
            "category_enumerate", "category_create", "category_read", "category_update", "category_delete",
            "memory_enumerate", "memory_read", "memory_upsert", "memory_search", "memory_delete",
            "endpoint_enumerate", "endpoint_read", "endpoint_create", "endpoint_update", "endpoint_delete", "endpoint_health",
            "chat",
            "collection_enumerate", "collection_read", "collection_create", "collection_delete",
            "instruction_create", "instruction_update", "instruction_delete"
        };

        private static void RequireExactTools(JsonElement result, string label)
        {
            HashSet<string> actual = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement tool in result.GetProperty("tools").EnumerateArray()) actual.Add(tool.GetProperty("name").GetString() ?? string.Empty);

            foreach (string tool in _ExpectedTools)
            {
                if (!actual.Contains(tool)) throw new InvalidOperationException(label + " is missing the '" + tool + "' tool (REST parity gap): " + result.GetRawText());
            }

            HashSet<string> extra = new HashSet<string>(actual, StringComparer.Ordinal);
            extra.ExceptWith(_ExpectedTools);
            if (extra.Count > 0) throw new InvalidOperationException(label + " publishes tools Isis does not register: " + string.Join(", ", extra));
        }

        private static bool IsFailure(RawResponse response)
        {
            if (response.StatusCode != HttpStatusCode.OK) return true;
            using JsonDocument doc = JsonDocument.Parse(response.Text);
            if (doc.RootElement.TryGetProperty("error", out _)) return true;
            return doc.RootElement.TryGetProperty("result", out JsonElement result)
                && result.TryGetProperty("isError", out JsonElement isError)
                && isError.ValueKind == JsonValueKind.True;
        }

        private static async Task<string?> InitializeAsync(HttpClient client, string accessKey)
        {
            RawResponse init = await SendRawAsync(client, accessKey, null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}").ConfigureAwait(false);
            if (init.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("initialize returned " + (int)init.StatusCode + ": " + init.Text);
            return init.SessionId;
        }

        private static async Task<RawResponse> SendRawAsync(HttpClient client, string? accessKey, string? session, string body)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            if (accessKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (!string.IsNullOrEmpty(session)) request.Headers.Add("Mcp-Session-Id", session);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string? sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? ids) ? System.Linq.Enumerable.FirstOrDefault(ids) : session;
            return new RawResponse(response.StatusCode, text, sessionId);
        }

        private static async Task EndpointCrudAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            string basePath = "/v1.0/api/tenants/ten_default/endpoints";

            string createBody = JsonSerializer.Serialize(new { name = "mcp-embed", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", authType = "None", model = "all-minilm", dimensionality = 384 });
            JsonElement created = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Post, basePath, createBody, "endpoint_create", ctx.Admin).ConfigureAwait(false), "endpoint_create");
            string id = created.GetProperty("id").GetString() ?? throw new InvalidOperationException("No endpoint id.");
            if (created.GetProperty("baseUrl").GetString() != "http://127.0.0.1:11434") throw new InvalidOperationException("baseUrl did not persist.");

            JsonElement read = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Get, basePath + "/" + id, null, "endpoint_read", ctx.Admin).ConfigureAwait(false), "endpoint_read");
            if (read.GetProperty("id").GetString() != id) throw new InvalidOperationException("endpoint_read returned the wrong id.");

            string updateBody = JsonSerializer.Serialize(new { name = "mcp-embed-2", baseUrl = "http://127.0.0.1:11434", authType = "BearerToken", authSecret = "tok" });
            JsonElement updated = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Put, basePath + "/" + id, updateBody, "endpoint_update", ctx.Admin).ConfigureAwait(false), "endpoint_update");
            if (updated.GetProperty("name").GetString() != "mcp-embed-2") throw new InvalidOperationException("endpoint_update did not persist the name.");
            if (updated.GetProperty("authType").GetString() != "BearerToken") throw new InvalidOperationException("endpoint_update did not persist authType.");

            Envelope del = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Delete, basePath + "/" + id, null, "endpoint_delete", ctx.Admin).ConfigureAwait(false));
            if (!del.Success) throw new InvalidOperationException("endpoint_delete failed with status " + del.StatusCode + ".");
        }

        private static async Task ScopeUpdateDeleteAsync()
        {
            using McpContext ctx = await McpContext.StartAsync().ConfigureAwait(false);
            string scopeId = await CreateScopeAsync(ctx).ConfigureAwait(false);
            string basePath = "/v1.0/api/tenants/ten_default/scopes/" + scopeId;

            string updateBody = JsonSerializer.Serialize(new { name = "mcpproj-renamed", description = "updated" });
            JsonElement updated = Data(await ctx.Mcp.ProxyAsync(HttpMethod.Put, basePath, updateBody, "scope_update", ctx.Admin).ConfigureAwait(false), "scope_update");
            if (updated.GetProperty("name").GetString() != "mcpproj-renamed") throw new InvalidOperationException("scope_update did not persist the name.");

            Envelope del = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Delete, basePath, null, "scope_delete", ctx.Admin).ConfigureAwait(false));
            if (!del.Success) throw new InvalidOperationException("scope_delete failed with status " + del.StatusCode + ".");

            Envelope read = Unpack(await ctx.Mcp.ProxyAsync(HttpMethod.Get, basePath, null, "scope_read", ctx.Admin).ConfigureAwait(false));
            if (read.Success || read.StatusCode != 404) throw new InvalidOperationException("Expected the deleted scope to be gone (404), got " + read.StatusCode + ".");
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

        private readonly struct RawResponse
        {
            internal RawResponse(HttpStatusCode statusCode, string text, string? sessionId)
            {
                StatusCode = statusCode;
                Text = text;
                SessionId = sessionId;
            }

            internal HttpStatusCode StatusCode { get; }

            internal string Text { get; }

            internal string? SessionId { get; }
        }

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
