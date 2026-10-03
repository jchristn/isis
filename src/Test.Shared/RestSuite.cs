namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using NotDory.Server.Routes;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end REST test suite for the NotDory server. Each case boots a real in-process NotDory REST server
    /// over a temporary SQLite database (via <see cref="ServerHarness"/>) and exercises one or more routes,
    /// asserting exact HTTP status codes and response shapes for both positive and negative paths.
    /// </summary>
    public static class RestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Get the REST Touchstone test suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "rest",
                "NotDory REST Suite",
                new List<TestCaseDescriptor>
                {
                    // System / health / discovery.
                    TestCase.Async("rest", "health-anon", "GET /health is anonymous and healthy", HealthAnonAsync),
                    TestCase.Async("rest", "server-info", "GET /server/info returns product and version", ServerInfoAsync),
                    TestCase.Async("rest", "openapi", "GET /openapi.json documents the tenants path", OpenApiAsync),

                    // Whoami.
                    TestCase.Async("rest", "whoami-admin", "GET /whoami as admin reports isAdmin", WhoAmIAdminAsync),
                    TestCase.Async("rest", "whoami-access", "GET /whoami as credential reports the tenant", WhoAmIAccessAsync),
                    TestCase.Async("rest", "whoami-anon", "GET /whoami anonymous is unauthorized", WhoAmIAnonAsync),

                    // Tenants.
                    TestCase.Async("rest", "tenants-anon-list", "GET /tenants anonymous is unauthorized", TenantsAnonListAsync),
                    TestCase.Async("rest", "tenants-create", "POST /tenants as admin creates a tenant", TenantsCreateAsync),
                    TestCase.Async("rest", "tenants-create-noname", "POST /tenants without a name is a bad request", TenantsCreateNoNameAsync),
                    TestCase.Async("rest", "tenants-create-nonadmin", "POST /tenants as a credential is forbidden", TenantsCreateNonAdminAsync),
                    TestCase.Async("rest", "tenants-list", "GET /tenants as admin lists tenants", TenantsListAsync),
                    TestCase.Async("rest", "tenants-read", "GET /tenants/{id} reads a tenant", TenantsReadAsync),
                    TestCase.Async("rest", "tenants-read-unknown", "GET /tenants/{unknown} is not found", TenantsReadUnknownAsync),
                    TestCase.Async("rest", "tenants-update", "PUT /tenants/{id} updates a tenant", TenantsUpdateAsync),
                    TestCase.Async("rest", "tenants-delete", "DELETE /tenants/{id} deletes a tenant", TenantsDeleteAsync),
                    TestCase.Async("rest", "tenants-delete-unknown", "DELETE /tenants/{unknown} is not found", TenantsDeleteUnknownAsync),
                    TestCase.Async("rest", "tenant-provision-related", "POST /tenants provisions a user, credential, and instructions", TenantProvisionRelatedAsync),
                    TestCase.Async("rest", "tenant-nuke-cascade", "DELETE /tenants/{id} cascades to all children and isolates other tenants", TenantNukeCascadeAsync),
                    TestCase.Async("rest", "tenant-nuke-protected", "DELETE the protected default tenant is a conflict", TenantNukeProtectedAsync),
                    TestCase.Async("rest", "tenant-nuke-nonadmin", "DELETE /tenants/{id} as a credential is forbidden", TenantNukeNonAdminAsync),
                    TestCase.Async("rest", "scope-delete-cascade", "DELETE /scopes/{id} cascades to categories and memories", ScopeDeleteCascadeAsync),
                    TestCase.Async("rest", "category-delete-cascade", "DELETE /categories/{id} cascades to its memories", CategoryDeleteCascadeAsync),
                    TestCase.Async("rest", "user-delete-cascade", "DELETE /users/{id} cascades to its credentials", UserDeleteCascadeAsync),
                    TestCase.Async("rest", "instructions-batch", "Batch create/get/delete instructions over REST", InstructionsBatchAsync),
                    TestCase.Async("rest", "instructions-scope-resolve", "Scope instructions merge onto the global set via effective-instructions", InstructionsScopeResolveAsync),
                    TestCase.Async("rest", "scope-batch-delete-cascade", "POST /scopes/batch-delete cascades to children", ScopeBatchDeleteCascadeAsync),

                    // Scopes.
                    TestCase.Async("rest", "scope-create", "POST /scopes as a credential creates a scope", ScopeCreateAsync),
                    TestCase.Async("rest", "scope-create-dup", "POST /scopes with a duplicate name conflicts", ScopeCreateDuplicateAsync),
                    TestCase.Async("rest", "scope-create-noname", "POST /scopes without a name is a bad request", ScopeCreateNoNameAsync),
                    TestCase.Async("rest", "scope-create-recalldb-auto", "POST /scopes RecallDb auto-wires the embedding endpoint", ScopeCreateRecallDbAutoWiresEndpointAsync),
                    TestCase.Async("rest", "scope-create-recalldb-auto-rerank", "POST /scopes RecallDb attaches the tenant's rerank endpoint", ScopeCreateRecallDbAutoRerankAsync),
                    TestCase.Async("rest", "scope-list", "GET /scopes lists scopes", ScopeListAsync),
                    TestCase.Async("rest", "scope-read", "GET /scopes/{id} reads a scope", ScopeReadAsync),
                    TestCase.Async("rest", "scope-read-unknown", "GET /scopes/{unknown} is not found", ScopeReadUnknownAsync),
                    TestCase.Async("rest", "scope-update", "PUT /scopes/{id} updates a scope", ScopeUpdateAsync),
                    TestCase.Async("rest", "scope-cross-tenant", "GET another tenant's scopes is forbidden", ScopeCrossTenantAsync),
                    TestCase.Async("rest", "scope-delete", "DELETE /scopes/{id} deletes a scope", ScopeDeleteAsync),
                    TestCase.Async("rest", "scope-delete-unknown", "DELETE /scopes/{unknown} is not found", ScopeDeleteUnknownAsync),

                    // Categories.
                    TestCase.Async("rest", "category-create", "POST /categories creates a category", CategoryCreateAsync),
                    TestCase.Async("rest", "category-create-dup", "POST /categories with a duplicate name conflicts", CategoryCreateDuplicateAsync),
                    TestCase.Async("rest", "category-list", "GET /categories lists categories", CategoryListAsync),
                    TestCase.Async("rest", "category-read", "GET /categories/{id} reads a category", CategoryReadAsync),
                    TestCase.Async("rest", "category-read-wrong-scope", "GET a category under the wrong scope is not found", CategoryReadWrongScopeAsync),
                    TestCase.Async("rest", "category-update", "PUT /categories/{id} updates a category", CategoryUpdateAsync),
                    TestCase.Async("rest", "category-delete", "DELETE /categories/{id} deletes a category", CategoryDeleteAsync),
                    TestCase.Async("rest", "category-delete-unknown", "DELETE /categories/{unknown} is not found", CategoryDeleteUnknownAsync),

                    // Memories.
                    TestCase.Async("rest", "memory-upsert", "POST /memories creates a memory with a store key", MemoryUpsertAsync),
                    TestCase.Async("rest", "memory-upsert-idempotent", "POST /memories twice is idempotent by slug", MemoryUpsertIdempotentAsync),
                    TestCase.Async("rest", "memory-upsert-noslug", "POST /memories without a slug is a bad request", MemoryUpsertNoSlugAsync),
                    TestCase.Async("rest", "memory-upsert-bad-category", "POST /memories with a foreign category is a bad request", MemoryUpsertBadCategoryAsync),
                    TestCase.Async("rest", "memory-list", "GET /memories lists memories by category", MemoryListAsync),
                    TestCase.Async("rest", "memory-read", "GET /memories/{id} reads a memory", MemoryReadAsync),
                    TestCase.Async("rest", "memory-read-unknown", "GET /memories/{unknown} is not found", MemoryReadUnknownAsync),
                    TestCase.Async("rest", "memory-search", "POST /memories/search returns hits", MemorySearchAsync),
                    TestCase.Async("rest", "memory-search-empty-query", "POST /memories/search without queryText is a bad request", MemorySearchEmptyQueryAsync),
                    TestCase.Async("rest", "memory-search-invalid-input", "POST /memories/search: null sub-query ignored, oversized query 400, huge token budget clamped", MemorySearchInvalidInputAsync),
                    TestCase.Async("rest", "body-too-large", "A request body over the server's limit is answered 413", BodyTooLargeAsync),
                    TestCase.Async("rest", "scope-filesystem-mirror", "Scope filesystemMirror is validated, stored, and creates its directory", ScopeFilesystemMirrorAsync),
                    TestCase.Async("rest", "scope-create-unknown-provider-rejected", "POST /scopes with an unknown store provider (such as the removed Verbex) is a bad request", ScopeCreateUnknownProviderRejectedAsync),
                    TestCase.Async("rest", "scope-models", "POST/PUT /scopes validate and persist the chat and query models and query settings", ScopeModelsAsync),
                    TestCase.Async("rest", "endpoint-invalid-base-url", "Endpoint create, update, and batch create reject a non-http base URL", EndpointInvalidBaseUrlAsync),
                    TestCase.Async("rest", "failure-recorded", "Failed requests are recorded once in request history: a route failure with its exception summary, and a request rejected during authentication", FailureRecordedAsync),
                    TestCase.Async("rest", "agent-protocol-read", "GET /agent-protocol is public and lists the default server instructions and every tool description", AgentProtocolReadAsync),
                    TestCase.Async("rest", "agent-protocol-edit", "PUT /agent-protocol: admin only, unknown tools rejected, edits saved to the settings file and used by session start, reset restores defaults", AgentProtocolEditAsync),
                    TestCase.Async("rest", "session-start", "POST /session matches the project's scope and returns protocol, categories, instructions, and recent memories; GET format=text renders markdown", SessionStartAsync),
                    TestCase.Async("rest", "session-start-choice", "Session start without a match or project lists scopes, and creates the project's scope when asked", SessionStartChoiceAsync),
                    TestCase.Async("rest", "session-start-mirror", "Session start mirrors a new scope to the project's .okf when the server can see the project, and says why when it cannot", SessionStartMirrorAsync),
                    TestCase.Async("rest", "session-start-remote", "Session start matches by the git remote's repository name before the folder name, creates a scope for a new repository, and never for a bare folder", SessionStartRemoteAsync),
                    TestCase.Async("rest", "memory-upsert-category-name", "Memory upsert accepts a category name (created once) and rejects an unknown cat_ id", MemoryUpsertCategoryNameAsync),
                    TestCase.Async("rest", "endpoint-reasoning", "Endpoint reasoning setting round-trips through create and update and defaults to Default", EndpointReasoningRestAsync),
                    TestCase.Async("rest", "endpoint-health-check-url", "Endpoint health check URL accepts a path or a full http URL and rejects other schemes", EndpointHealthCheckUrlAsync),
                    TestCase.Async("rest", "memory-search-category-name", "POST /memories/search filters by category name", MemorySearchCategoryByNameAsync),
                    TestCase.Async("rest", "memory-search-category-id", "POST /memories/search filters by category id", MemorySearchCategoryByIdAsync),
                    TestCase.Async("rest", "memory-search-category-unknown", "POST /memories/search with an unknown category is a bad request", MemorySearchCategoryUnknownAsync),
                    TestCase.Async("rest", "memory-delete", "DELETE /memories/{id} deletes a memory", MemoryDeleteAsync),
                    TestCase.Async("rest", "memory-delete-unknown", "DELETE /memories/{unknown} is not found", MemoryDeleteUnknownAsync),

                    // Model endpoints.
                    TestCase.Async("rest", "endpoint-create-embedding", "POST /endpoints creates an embedding endpoint", EndpointCreateEmbeddingAsync),
                    TestCase.Async("rest", "endpoint-create-inference", "POST /endpoints creates an inference endpoint", EndpointCreateInferenceAsync),
                    TestCase.Async("rest", "endpoint-list", "GET /endpoints lists endpoints", EndpointListAsync),
                    TestCase.Async("rest", "endpoint-list-kind", "GET /endpoints?kind=Embedding filters by kind", EndpointListByKindAsync),
                    TestCase.Async("rest", "endpoint-read", "GET /endpoints/{id} reads an endpoint", EndpointReadAsync),
                    TestCase.Async("rest", "endpoint-update", "PUT /endpoints/{id} updates an endpoint", EndpointUpdateAsync),
                    TestCase.Async("rest", "endpoint-delete", "DELETE /endpoints/{id} deletes an endpoint", EndpointDeleteAsync),
                    TestCase.Async("rest", "endpoint-health", "GET /endpoint-health probes endpoints", EndpointHealthAsync),

                    // Chat.
                    TestCase.Async("rest", "chat-no-endpoint", "POST /chat without an inference endpoint is a bad request", ChatNoEndpointAsync),
                    TestCase.Async("rest", "chat-no-question", "POST /chat without a question is a bad request", ChatNoQuestionAsync),

                    // Collections.
                    TestCase.Async("rest", "collections-no-recalldb", "GET /collections without RecallDB is a bad request", CollectionsNoRecallDbAsync),

                    // Guide.
                    TestCase.Async("rest", "guide", "GET /guide returns categories, capabilities, instructions", GuideAsync),

                    // Request history.
                    TestCase.Async("rest", "requests-list", "GET /requests lists captured traffic excluding health", RequestsListAsync),
                    TestCase.Async("rest", "requests-clear", "DELETE /requests clears history", RequestsClearAsync),
                    TestCase.Async("rest", "requests-anon", "GET /requests anonymous is unauthorized", RequestsAnonAsync)
                });
        }

        #endregion

        #region Private-Methods-System

        private static async Task HealthAnonAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync(Api + "/health").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "health anon");
        }

        private static async Task ServerInfoAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync(Api + "/server/info").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "server info");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("product", out _), "server info should expose 'product'.");
            TestCase.Require(doc.RootElement.TryGetProperty("version", out _), "server info should expose 'version'.");
        }

        private static async Task OpenApiAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync("/openapi.json").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "openapi");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("paths", out JsonElement paths), "openapi should have a 'paths' object.");
            TestCase.Require(paths.TryGetProperty("/v1.0/api/tenants", out _), "openapi paths should document /v1.0/api/tenants.");
        }

        #endregion

        #region Private-Methods-Whoami

        private static async Task WhoAmIAdminAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.GetAsync(Api + "/whoami").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "whoami admin");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("isAdmin").GetBoolean(), "admin whoami should report isAdmin=true.");
        }

        private static async Task WhoAmIAccessAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await access.GetAsync(Api + "/whoami").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "whoami access");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("tenantId").GetString() == h.TenantId, "credential whoami should resolve tenant '" + h.TenantId + "'.");
        }

        private static async Task WhoAmIAnonAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync(Api + "/whoami").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Unauthorized, "whoami anon");
        }

        #endregion

        #region Private-Methods-Tenants

        private static async Task TenantsAnonListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync(Tenants).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Unauthorized, "anon list tenants");
        }

        private static async Task TenantsCreateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await PostAsync(admin, Tenants, new { name = "Acme" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create tenant");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("tenant").GetProperty("id").GetString()), "created tenant should have an id.");
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("admin").GetProperty("email").GetString()), "provisioning should return an admin email.");
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("admin").GetProperty("password").GetString()), "provisioning should return a one-time admin password.");
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("credential").GetProperty("secretKey").GetString()), "provisioning should return a one-time credential secret.");
        }

        private static async Task TenantsCreateNoNameAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await PostAsync(admin, Tenants, new { }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "create tenant without name");
        }

        private static async Task TenantsCreateNonAdminAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, Tenants, new { name = "Nope" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Forbidden, "create tenant as credential");
        }

        private static async Task TenantsListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.GetAsync(Tenants).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list tenants");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("objects", out _), "tenant list should be an enumeration result.");
        }

        private static async Task TenantsReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.GetAsync(Tenants + "/" + h.TenantId).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "read tenant");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("id").GetString() == h.TenantId, "read tenant should return the requested tenant.");
        }

        private static async Task TenantsReadUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.GetAsync(Tenants + "/ten_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "read unknown tenant");
        }

        private static async Task TenantsUpdateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string id = await CreateTenantAsync(admin, "ToUpdate").ConfigureAwait(false);
            HttpResponseMessage r = await PutAsync(admin, Tenants + "/" + id, new { name = "Updated" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "update tenant");
        }

        private static async Task TenantsDeleteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string id = await CreateTenantAsync(admin, "ToDelete").ConfigureAwait(false);
            HttpResponseMessage r = await admin.DeleteAsync(Tenants + "/" + id).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NoContent, "delete tenant");
        }

        private static async Task TenantsDeleteUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.DeleteAsync(Tenants + "/ten_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "delete unknown tenant");
        }

        private static async Task<int> CountAsync(HttpClient client, string path)
        {
            HttpResponseMessage r = await client.GetAsync(path).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "count " + path);
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("totalRecords").GetInt32();
        }

        private static async Task TenantProvisionRelatedAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string id = await CreateTenantAsync(admin, "Provisioned").ConfigureAwait(false);

            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/users").ConfigureAwait(false) >= 1, "a provisioned tenant should have an admin user.");
            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/credentials").ConfigureAwait(false) >= 1, "a provisioned tenant should have a credential.");
            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/instructions").ConfigureAwait(false) >= 1, "a provisioned tenant should have default instructions.");
        }

        private static async Task TenantNukeCascadeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string id = await CreateTenantAsync(admin, "Doomed").ConfigureAwait(false);

            // Build a full object graph under the new tenant.
            HttpResponseMessage scopeResp = await PostAsync(admin, ScopesPath(id), new { name = "s1", storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = Path.Combine(h.WorkDir, "nuke-" + id) }).ConfigureAwait(false);
            ExpectStatus(scopeResp, HttpStatusCode.Created, "create scope in doomed tenant");
            string scopeId;
            using (JsonDocument sd = await ReadJsonAsync(scopeResp).ConfigureAwait(false)) scopeId = sd.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage catResp = await PostAsync(admin, CategoriesPath(id, scopeId), new { name = "notes", instructions = "x" }).ConfigureAwait(false);
            ExpectStatus(catResp, HttpStatusCode.Created, "create category in doomed tenant");
            string categoryId;
            using (JsonDocument cd = await ReadJsonAsync(catResp).ConfigureAwait(false)) categoryId = cd.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage memResp = await PostAsync(admin, MemoriesPath(id, scopeId), new { categoryId, slug = "m1", title = "m1", body = "hello" }).ConfigureAwait(false);
            ExpectStatus(memResp, HttpStatusCode.OK, "upsert memory in doomed tenant");
            await PostAsync(admin, TenantPath(id) + "/users", new { email = "extra@x.local", password = "pw123456" }).ConfigureAwait(false);
            await PostAsync(admin, EndpointsPath(id), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://localhost:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);

            // Sanity: children exist.
            TestCase.Require(await CountAsync(admin, ScopesPath(id)).ConfigureAwait(false) >= 1, "doomed tenant should have a scope before nuke.");

            // Baseline for isolation: the default tenant's data must survive the nuke.
            int defaultInstructionsBefore = await CountAsync(admin, TenantPath(h.TenantId) + "/instructions").ConfigureAwait(false);

            HttpResponseMessage del = await admin.DeleteAsync(TenantPath(id)).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.NoContent, "nuke tenant");

            // Tenant and every child are gone.
            HttpResponseMessage read = await admin.GetAsync(TenantPath(id)).ConfigureAwait(false);
            ExpectStatus(read, HttpStatusCode.NotFound, "nuked tenant should be gone");
            TestCase.Require(await CountAsync(admin, ScopesPath(id)).ConfigureAwait(false) == 0, "nuked tenant should have no scopes.");
            TestCase.Require(await CountAsync(admin, CategoriesPath(id, scopeId)).ConfigureAwait(false) == 0, "nuked tenant should have no categories.");
            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/users").ConfigureAwait(false) == 0, "nuked tenant should have no users.");
            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/credentials").ConfigureAwait(false) == 0, "nuked tenant should have no credentials.");
            TestCase.Require(await CountAsync(admin, TenantPath(id) + "/instructions").ConfigureAwait(false) == 0, "nuked tenant should have no instructions.");
            TestCase.Require(await CountAsync(admin, EndpointsPath(id)).ConfigureAwait(false) == 0, "nuked tenant should have no endpoints.");

            // Cross-tenant isolation: the default tenant is untouched.
            TestCase.Require(await CountAsync(admin, TenantPath(h.TenantId) + "/instructions").ConfigureAwait(false) == defaultInstructionsBefore, "nuking one tenant must not affect another tenant's data.");
        }

        private static async Task TenantNukeProtectedAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.DeleteAsync(TenantPath(h.TenantId)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Conflict, "nuke protected default tenant");
        }

        private static async Task TenantNukeNonAdminAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string id = await CreateTenantAsync(admin, "GuardedNuke").ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await access.DeleteAsync(TenantPath(id)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Forbidden, "nuke tenant as credential");
        }

        private static async Task ScopeDeleteCascadeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string scopeId = await CreateScopeAsync(admin, h, "cascadescope").ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(admin, h, scopeId, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(admin, h, scopeId, categoryId, "m1", "hello").ConfigureAwait(false);

            TestCase.Require(await CountAsync(admin, CategoriesPath(h.TenantId, scopeId)).ConfigureAwait(false) >= 1, "scope should have a category before delete.");

            HttpResponseMessage del = await admin.DeleteAsync(ScopesPath(h.TenantId) + "/" + scopeId).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.NoContent, "delete scope");

            TestCase.Require(await CountAsync(admin, CategoriesPath(h.TenantId, scopeId)).ConfigureAwait(false) == 0, "deleting a scope should cascade to its categories.");
            TestCase.Require(await CountAsync(admin, MemoriesPath(h.TenantId, scopeId)).ConfigureAwait(false) == 0, "deleting a scope should cascade to its memories.");
        }

        private static async Task CategoryDeleteCascadeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string scopeId = await CreateScopeAsync(admin, h, "catcascade").ConfigureAwait(false);
            string categoryId = await CreateCategoryAsync(admin, h, scopeId, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(admin, h, scopeId, categoryId, "m1", "hello").ConfigureAwait(false);

            TestCase.Require(await CountAsync(admin, MemoriesPath(h.TenantId, scopeId)).ConfigureAwait(false) >= 1, "category should have a memory before delete.");

            HttpResponseMessage del = await admin.DeleteAsync(CategoriesPath(h.TenantId, scopeId) + "/" + categoryId).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.NoContent, "delete category");

            TestCase.Require(await CountAsync(admin, MemoriesPath(h.TenantId, scopeId)).ConfigureAwait(false) == 0, "deleting a category should cascade to its memories.");
        }

        private static async Task UserDeleteCascadeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();

            HttpResponseMessage userResp = await PostAsync(admin, TenantPath(h.TenantId) + "/users", new { email = "casc@x.local", password = "pw123456" }).ConfigureAwait(false);
            ExpectStatus(userResp, HttpStatusCode.Created, "create user");
            string userId;
            using (JsonDocument ud = await ReadJsonAsync(userResp).ConfigureAwait(false)) userId = ud.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage credResp = await PostAsync(admin, TenantPath(h.TenantId) + "/credentials", new { name = "owned", userId }).ConfigureAwait(false);
            ExpectStatus(credResp, HttpStatusCode.Created, "create owned credential");
            string credentialId;
            using (JsonDocument cd = await ReadJsonAsync(credResp).ConfigureAwait(false)) credentialId = cd.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage before = await admin.GetAsync(TenantPath(h.TenantId) + "/credentials/" + credentialId).ConfigureAwait(false);
            ExpectStatus(before, HttpStatusCode.OK, "owned credential exists before user delete");

            HttpResponseMessage del = await admin.DeleteAsync(TenantPath(h.TenantId) + "/users/" + userId).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.NoContent, "delete user");

            HttpResponseMessage after = await admin.GetAsync(TenantPath(h.TenantId) + "/credentials/" + credentialId).ConfigureAwait(false);
            ExpectStatus(after, HttpStatusCode.NotFound, "deleting a user should cascade to its credentials");
        }

        private static async Task InstructionsScopeResolveAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();

            HttpResponseMessage scopeResp = await PostAsync(admin, ScopesPath(h.TenantId), NewScope(h, "instr-scope")).ConfigureAwait(false);
            ExpectStatus(scopeResp, HttpStatusCode.Created, "create scope");
            string scopeId;
            using (JsonDocument sd = await ReadJsonAsync(scopeResp).ConfigureAwait(false)) scopeId = sd.RootElement.GetProperty("id").GetString()!;

            string tenantInstr = TenantPath(h.TenantId) + "/instructions";
            string scopeInstr = ScopesPath(h.TenantId) + "/" + scopeId + "/instructions";

            // Two tenant-global instructions. Names are deliberately unique so they do not collide with the
            // tenant's seeded default instruction set.
            ExpectStatus(await PostAsync(admin, tenantInstr, new { name = "E2E-Tools", content = "global tools", position = 100 }).ConfigureAwait(false), HttpStatusCode.Created, "global E2E-Tools");
            ExpectStatus(await PostAsync(admin, tenantInstr, new { name = "E2E-Start", content = "global start", position = 101 }).ConfigureAwait(false), HttpStatusCode.Created, "global E2E-Start");

            // Scope: replace E2E-Tools, hide E2E-Start, append E2E-ScopeOnly.
            ExpectStatus(await PostAsync(admin, scopeInstr, new { name = "E2E-Tools", content = "scope tools", position = 0, mergeMode = "Replace" }).ConfigureAwait(false), HttpStatusCode.Created, "scope replace E2E-Tools");
            ExpectStatus(await PostAsync(admin, scopeInstr, new { name = "E2E-Start", content = "", position = 1, mergeMode = "Hide" }).ConfigureAwait(false), HttpStatusCode.Created, "scope hide E2E-Start");
            ExpectStatus(await PostAsync(admin, scopeInstr, new { name = "E2E-ScopeOnly", content = "only here", position = 2, mergeMode = "Append" }).ConfigureAwait(false), HttpStatusCode.Created, "scope append E2E-ScopeOnly");

            HttpResponseMessage eff = await admin.GetAsync(ScopesPath(h.TenantId) + "/" + scopeId + "/effective-instructions").ConfigureAwait(false);
            ExpectStatus(eff, HttpStatusCode.OK, "resolve effective instructions");
            using JsonDocument ed = await ReadJsonAsync(eff).ConfigureAwait(false);
            JsonElement objects = ed.RootElement.GetProperty("objects");

            bool sawToolsOverride = false;
            bool sawStart = false;
            bool sawScopeOnly = false;
            foreach (JsonElement e in objects.EnumerateArray())
            {
                string name = e.GetProperty("name").GetString()!;
                if (name == "E2E-Tools")
                {
                    sawToolsOverride = e.GetProperty("content").GetString() == "scope tools" && e.GetProperty("source").GetString() == "ScopeOverride";
                }
                if (name == "E2E-Start") sawStart = true;
                if (name == "E2E-ScopeOnly") sawScopeOnly = e.GetProperty("source").GetString() == "ScopeAdded";
            }

            TestCase.Require(sawToolsOverride, "Effective 'E2E-Tools' must be overridden by the scope (content 'scope tools', source ScopeOverride).");
            TestCase.Require(!sawStart, "Effective set must not contain the hidden 'E2E-Start' instruction.");
            TestCase.Require(sawScopeOnly, "Effective set must contain the appended 'E2E-ScopeOnly' instruction.");
        }

        private static async Task InstructionsBatchAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            string basePath = TenantPath(h.TenantId) + "/instructions";

            HttpResponseMessage create = await PostAsync(admin, basePath + "/batch", new { items = new[] { new { name = "b1", content = "x", position = 10 }, new { name = "b2", content = "y", position = 11 } } }).ConfigureAwait(false);
            ExpectStatus(create, HttpStatusCode.Created, "batch create instructions");
            List<string> ids = new List<string>();
            using (JsonDocument cd = await ReadJsonAsync(create).ConfigureAwait(false))
            {
                foreach (JsonElement e in cd.RootElement.GetProperty("objects").EnumerateArray()) ids.Add(e.GetProperty("id").GetString()!);
            }
            TestCase.Require(ids.Count == 2, "batch create should return both created instructions.");

            HttpResponseMessage get = await PostAsync(admin, basePath + "/batch-get", new { ids }).ConfigureAwait(false);
            ExpectStatus(get, HttpStatusCode.OK, "batch-get instructions");
            using (JsonDocument gd = await ReadJsonAsync(get).ConfigureAwait(false))
            {
                TestCase.Require(gd.RootElement.GetProperty("objects").GetArrayLength() == 2, "batch-get should return both ids.");
            }

            HttpResponseMessage del = await PostAsync(admin, basePath + "/batch-delete", new { ids }).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.OK, "batch-delete instructions");
            using (JsonDocument dd = await ReadJsonAsync(del).ConfigureAwait(false))
            {
                TestCase.Require(dd.RootElement.GetProperty("deleted").GetInt32() == 2, "batch-delete should report both deletions.");
            }

            HttpResponseMessage getAfter = await PostAsync(admin, basePath + "/batch-get", new { ids }).ConfigureAwait(false);
            using (JsonDocument gd2 = await ReadJsonAsync(getAfter).ConfigureAwait(false))
            {
                TestCase.Require(gd2.RootElement.GetProperty("objects").GetArrayLength() == 0, "batch-deleted instructions should be gone.");
            }
        }

        private static async Task ScopeBatchDeleteCascadeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();

            string scope1 = await CreateScopeAsync(admin, h, "bd1").ConfigureAwait(false);
            string cat1 = await CreateCategoryAsync(admin, h, scope1, "n1").ConfigureAwait(false);
            await UpsertMemoryAsync(admin, h, scope1, cat1, "m1", "a").ConfigureAwait(false);
            string scope2 = await CreateScopeAsync(admin, h, "bd2").ConfigureAwait(false);
            string cat2 = await CreateCategoryAsync(admin, h, scope2, "n2").ConfigureAwait(false);
            await UpsertMemoryAsync(admin, h, scope2, cat2, "m2", "b").ConfigureAwait(false);

            HttpResponseMessage del = await PostAsync(admin, ScopesPath(h.TenantId) + "/batch-delete", new { ids = new[] { scope1, scope2 } }).ConfigureAwait(false);
            ExpectStatus(del, HttpStatusCode.OK, "batch-delete scopes");
            using (JsonDocument dd = await ReadJsonAsync(del).ConfigureAwait(false))
            {
                TestCase.Require(dd.RootElement.GetProperty("deleted").GetInt32() == 2, "batch-delete should report both scopes.");
            }

            HttpResponseMessage read1 = await admin.GetAsync(ScopesPath(h.TenantId) + "/" + scope1).ConfigureAwait(false);
            ExpectStatus(read1, HttpStatusCode.NotFound, "batch-deleted scope should be gone");
            TestCase.Require(await CountAsync(admin, CategoriesPath(h.TenantId, scope1)).ConfigureAwait(false) == 0, "batch-deleting a scope should cascade to its categories.");
            TestCase.Require(await CountAsync(admin, MemoriesPath(h.TenantId, scope1)).ConfigureAwait(false) == 0, "batch-deleting a scope should cascade to its memories.");
        }

        #endregion

        #region Private-Methods-Scopes

        private static async Task ScopeCreateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId), NewScope(h, "s1")).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create scope");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("id").GetString()), "created scope should have an id.");
        }

        private static async Task ScopeCreateDuplicateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId), NewScope(h, "s1")).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Conflict, "duplicate scope");
        }

        private static async Task ScopeCreateNoNameAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId), new { }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "scope without name");
        }

        private static async Task ScopeCreateRecallDbAutoWiresEndpointAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();

            // With no embedding endpoint configured, a default (RecallDb) scope is rejected with guidance
            // rather than persisted broken.
            HttpResponseMessage noEndpoint = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "recall-noep" }).ConfigureAwait(false);
            ExpectStatus(noEndpoint, HttpStatusCode.BadRequest, "RecallDb scope with no embedding endpoint");

            // After adding an embedding endpoint, a bare RecallDb scope adopts it and its dimensionality.
            HttpResponseMessage epResp = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://localhost:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(epResp, HttpStatusCode.Created, "create embedding endpoint");
            string endpointId;
            using (JsonDocument ed = await ReadJsonAsync(epResp).ConfigureAwait(false)) endpointId = ed.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage scopeResp = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "recall-auto" }).ConfigureAwait(false);
            ExpectStatus(scopeResp, HttpStatusCode.Created, "RecallDb scope auto-wires endpoint");
            using JsonDocument sd = await ReadJsonAsync(scopeResp).ConfigureAwait(false);
            TestCase.Require(sd.RootElement.GetProperty("storeProvider").GetString() == "RecallDb", "default store should be RecallDb.");
            TestCase.Require(sd.RootElement.GetProperty("embeddingEndpointId").GetString() == endpointId, "scope should adopt the tenant's embedding endpoint.");
            TestCase.Require(sd.RootElement.GetProperty("dimensionality").GetInt32() == 384, "scope should adopt the endpoint's dimensionality (384).");
        }

        private static async Task ScopeCreateRecallDbAutoRerankAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage emb = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(emb, HttpStatusCode.Created, "create embedding endpoint");
            HttpResponseMessage chat = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "chat", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "gemma3:4b" }).ConfigureAwait(false);
            ExpectStatus(chat, HttpStatusCode.Created, "create chat endpoint");

            HttpResponseMessage noRerank = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "recall-no-rerank" }).ConfigureAwait(false);
            ExpectStatus(noRerank, HttpStatusCode.Created, "scope without a tenant cross-encoder");
            using (JsonDocument nd = await ReadJsonAsync(noRerank).ConfigureAwait(false))
            {
                TestCase.Require(!nd.RootElement.TryGetProperty("rerankEndpointId", out JsonElement none) || none.ValueKind == JsonValueKind.Null, "A chat model should never be attached as the reranker automatically.");
            }

            HttpResponseMessage rr = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "rr", kind = "Inference", apiFormat = "Tei", baseUrl = "http://127.0.0.1:18800", model = "ms-marco" }).ConfigureAwait(false);
            ExpectStatus(rr, HttpStatusCode.Created, "create rerank endpoint");
            string rerankId;
            using (JsonDocument rd = await ReadJsonAsync(rr).ConfigureAwait(false)) rerankId = rd.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage scope = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "recall-rerank" }).ConfigureAwait(false);
            ExpectStatus(scope, HttpStatusCode.Created, "scope with a tenant reranker");
            using JsonDocument sd = await ReadJsonAsync(scope).ConfigureAwait(false);
            TestCase.Require(sd.RootElement.GetProperty("rerankEndpointId").GetString() == rerankId, "A new RecallDb scope should attach the tenant's cross-encoder.");
        }

        private static async Task ScopeListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(ScopesPath(h.TenantId)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list scopes");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() >= 1, "scope list should include the created scope.");
        }

        private static async Task ScopeReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(ScopesPath(h.TenantId) + "/" + sid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "read scope");
        }

        private static async Task ScopeReadUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await access.GetAsync(ScopesPath(h.TenantId) + "/scp_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "read unknown scope");
        }

        private static async Task ScopeUpdateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PutAsync(access, ScopesPath(h.TenantId) + "/" + sid, new { name = "s1", description = "updated description" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "update scope");
        }

        private static async Task ScopeCrossTenantAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await access.GetAsync(ScopesPath("ten_other")).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Forbidden, "cross-tenant scope access");
        }

        private static async Task ScopeDeleteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(ScopesPath(h.TenantId) + "/" + sid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NoContent, "delete scope");
        }

        private static async Task ScopeDeleteUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await access.DeleteAsync(ScopesPath(h.TenantId) + "/scp_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "delete unknown scope");
        }

        #endregion

        #region Private-Methods-Categories

        private static async Task CategoryCreateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, CategoriesPath(h.TenantId, sid), new { name = "notes", instructions = "One idea per memory." }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create category");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("id").GetString()), "created category should have an id.");
        }

        private static async Task CategoryCreateDuplicateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, CategoriesPath(h.TenantId, sid), new { name = "notes" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Conflict, "duplicate category");
        }

        private static async Task CategoryListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(CategoriesPath(h.TenantId, sid)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list categories");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() >= 1, "category list should include the created category.");
        }

        private static async Task CategoryReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(CategoriesPath(h.TenantId, sid) + "/" + cid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "read category");
        }

        private static async Task CategoryReadWrongScopeAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string otherScope = await CreateScopeAsync(access, h, "s2").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(CategoriesPath(h.TenantId, otherScope) + "/" + cid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "read category under wrong scope");
        }

        private static async Task CategoryUpdateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await PutAsync(access, CategoriesPath(h.TenantId, sid) + "/" + cid, new { name = "notes", instructions = "Updated instructions." }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "update category");
        }

        private static async Task CategoryDeleteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(CategoriesPath(h.TenantId, sid) + "/" + cid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NoContent, "delete category");
        }

        private static async Task CategoryDeleteUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(CategoriesPath(h.TenantId, sid) + "/cat_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "delete unknown category");
        }

        #endregion

        #region Private-Methods-Memories

        private static async Task MemoryUpsertAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid),
                new { categoryId = cid, slug = "m1", title = "Centerline", body = "Control the centerline; posture and framing win positions." }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "upsert memory");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("slug").GetString() == "m1", "memory slug should round trip.");
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("storeKey").GetString()), "memory should carry a store key.");
        }

        private static async Task MemoryUpsertIdempotentAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);

            string firstId;
            HttpResponseMessage r1 = await PostAsync(access, MemoriesPath(h.TenantId, sid),
                new { categoryId = cid, slug = "m1", title = "V1", body = "first" }).ConfigureAwait(false);
            ExpectStatus(r1, HttpStatusCode.OK, "first upsert");
            using (JsonDocument d1 = await ReadJsonAsync(r1).ConfigureAwait(false))
            {
                firstId = d1.RootElement.GetProperty("id").GetString() ?? string.Empty;
            }

            HttpResponseMessage r2 = await PostAsync(access, MemoriesPath(h.TenantId, sid),
                new { categoryId = cid, slug = "m1", title = "V2", body = "second" }).ConfigureAwait(false);
            ExpectStatus(r2, HttpStatusCode.OK, "second upsert");
            using JsonDocument d2 = await ReadJsonAsync(r2).ConfigureAwait(false);
            TestCase.Require(d2.RootElement.GetProperty("id").GetString() == firstId, "re-upserting the same slug must reuse the id.");
            TestCase.Require(!string.IsNullOrEmpty(d2.RootElement.GetProperty("storeKey").GetString()), "idempotent upsert should still carry a store key.");
        }

        private static async Task MemoryUpsertNoSlugAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid),
                new { categoryId = cid, title = "No slug", body = "body only" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "memory without slug");
        }

        private static async Task MemoryUpsertBadCategoryAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid),
                new { categoryId = "cat_not_in_scope", slug = "m1", title = "T", body = "B" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "memory with foreign category");
        }

        private static async Task MemoryListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, cid, "m1", "Body about posture and framing.").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(MemoriesPath(h.TenantId, sid) + "?category=" + cid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list memories");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() >= 1, "memory list should include the created memory.");
        }

        private static async Task MemoryReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            string mid = await UpsertMemoryAsync(access, h, sid, cid, "m1", "Body about posture and framing.").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(MemoriesPath(h.TenantId, sid) + "/" + mid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "read memory");
        }

        private static async Task MemoryReadUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(MemoriesPath(h.TenantId, sid) + "/mem_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "read unknown memory");
        }

        private static async Task MemorySearchAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, cid, "centerline", "Control the centerline; posture and framing win positions.").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search",
                new { queryText = "posture framing", mode = "Keyword", topK = 5 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "search memories");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("hits").GetArrayLength() >= 1, "search should return at least one hit.");
        }

        private static async Task MemorySearchInvalidInputAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, cid, "centerline", "Control the centerline; posture and framing win positions.").ConfigureAwait(false);
            string path = MemoriesPath(h.TenantId, sid) + "/search";

            HttpResponseMessage nullSub = await PostAsync(access, path, new { queryText = "posture", mode = "Keyword", subQueries = new object?[] { null, new { text = "framing", weight = 0.5 } }, tokenBudget = int.MaxValue }).ConfigureAwait(false);
            ExpectStatus(nullSub, HttpStatusCode.OK, "search with a null sub-query and a huge token budget");
            using (JsonDocument doc = await ReadJsonAsync(nullSub).ConfigureAwait(false))
            {
                TestCase.Require(doc.RootElement.GetProperty("hits").GetArrayLength() >= 1, "The search should still return hits.");
            }

            HttpResponseMessage tooLong = await PostAsync(access, path, new { queryText = new string('q', 5000) }).ConfigureAwait(false);
            ExpectStatus(tooLong, HttpStatusCode.BadRequest, "search with a 5000-character query");
        }

        private static async Task BodyTooLargeAsync()
        {
            long previous = RouteHelpers.MaxBodyBytes;
            RouteHelpers.MaxBodyBytes = 2048;
            try
            {
                using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
                RouteHelpers.MaxBodyBytes = 2048;
                using HttpClient access = h.AccessClient();
                HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId), new { name = "big", description = new string('d', 4000), storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "big") }).ConfigureAwait(false);
                ExpectStatus(r, (HttpStatusCode)413, "oversized body");
            }
            finally
            {
                RouteHelpers.MaxBodyBytes = previous;
            }
        }

        private static async Task ScopeModelsAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage inference = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "chat", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "gemma3:4b" }).ConfigureAwait(false);
            ExpectStatus(inference, HttpStatusCode.Created, "create inference endpoint");
            string inferenceId;
            using (JsonDocument doc = await ReadJsonAsync(inference).ConfigureAwait(false)) inferenceId = doc.RootElement.GetProperty("id").GetString()!;
            HttpResponseMessage embedding = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            string embeddingId;
            using (JsonDocument doc = await ReadJsonAsync(embedding).ConfigureAwait(false)) embeddingId = doc.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage wrongKind = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "wrong", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "wrong"), queryEndpointId = embeddingId }).ConfigureAwait(false);
            ExpectStatus(wrongKind, HttpStatusCode.BadRequest, "query model that is an embedding endpoint");

            HttpResponseMessage tei = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "ce", kind = "Inference", apiFormat = "Tei", baseUrl = "http://127.0.0.1:18800", model = "ms-marco" }).ConfigureAwait(false);
            string teiId;
            using (JsonDocument doc = await ReadJsonAsync(tei).ConfigureAwait(false)) teiId = doc.RootElement.GetProperty("id").GetString()!;
            HttpResponseMessage gemini = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "gem", kind = "Inference", apiFormat = "Gemini", baseUrl = "https://generativelanguage.googleapis.com", model = "gemini" }).ConfigureAwait(false);
            string geminiId;
            using (JsonDocument doc = await ReadJsonAsync(gemini).ConfigureAwait(false)) geminiId = doc.RootElement.GetProperty("id").GetString()!;

            HttpResponseMessage crossEncoderChat = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "ce-chat", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "ce-chat"), inferenceEndpointId = teiId }).ConfigureAwait(false);
            ExpectStatus(crossEncoderChat, HttpStatusCode.BadRequest, "a cross-encoder as the chat model");
            HttpResponseMessage geminiRerank = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "gem-rerank", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "gem-rerank"), rerankEndpointId = geminiId }).ConfigureAwait(false);
            ExpectStatus(geminiRerank, HttpStatusCode.Created, "a Gemini chat model as the reranker");
            HttpResponseMessage embeddingRerank = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "emb-rerank", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "emb-rerank"), rerankEndpointId = embeddingId }).ConfigureAwait(false);
            ExpectStatus(embeddingRerank, HttpStatusCode.BadRequest, "an embedding endpoint as the reranker");
            HttpResponseMessage chatRerank = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "chat-rerank", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "chat-rerank"), rerankEndpointId = inferenceId, rerankMinScore = 0.5 }).ConfigureAwait(false);
            ExpectStatus(chatRerank, HttpStatusCode.Created, "a chat model as the reranker (high-precision mode)");

            HttpResponseMessage created = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "models", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "models"), inferenceEndpointId = inferenceId, queryEndpointId = inferenceId, queryExpansion = "On", conversationRewrite = false, queryDecomposition = true }).ConfigureAwait(false);
            ExpectStatus(created, HttpStatusCode.Created, "scope with models");
            using JsonDocument sd = await ReadJsonAsync(created).ConfigureAwait(false);
            JsonElement root = sd.RootElement;
            TestCase.Require(root.GetProperty("inferenceEndpointId").GetString() == inferenceId && root.GetProperty("queryEndpointId").GetString() == inferenceId, "The models should be stored.");
            TestCase.Require(root.GetProperty("queryExpansion").GetString() == "On" && !root.GetProperty("conversationRewrite").GetBoolean() && root.GetProperty("queryDecomposition").GetBoolean(), "The query settings should be stored.");
        }

        private static async Task ScopeFilesystemMirrorAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage emb = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(emb, HttpStatusCode.Created, "create embedding endpoint");

            HttpResponseMessage onFilesystem = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "fs-mirror", storeProvider = "Filesystem", targetPath = Path.Combine(h.WorkDir, "fs-mirror"), filesystemMirror = true }).ConfigureAwait(false);
            ExpectStatus(onFilesystem, HttpStatusCode.BadRequest, "filesystemMirror on a Filesystem scope");
            HttpResponseMessage noPath = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "no-path", filesystemMirror = true }).ConfigureAwait(false);
            ExpectStatus(noPath, HttpStatusCode.BadRequest, "filesystemMirror without a targetPath");
            HttpResponseMessage tilde = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "tilde", targetPath = "~/Code/repo" }).ConfigureAwait(false);
            ExpectStatus(tilde, HttpStatusCode.BadRequest, "a targetPath starting with '~'");

            string defaultDir = Path.Combine(h.WorkDir, "default-repo");
            using (JsonDocument doc = await ReadJsonAsync(await PostAsync(admin, ScopesPath(h.TenantId), new { name = "by-default", targetPath = defaultDir }).ConfigureAwait(false)).ConfigureAwait(false))
            {
                TestCase.Require(doc.RootElement.GetProperty("filesystemMirror").GetBoolean(), "A RecallDb scope given a targetPath should mirror by default.");
            }

            using (JsonDocument doc = await ReadJsonAsync(await PostAsync(admin, ScopesPath(h.TenantId), new { name = "opted-out", targetPath = defaultDir, filesystemMirror = false }).ConfigureAwait(false)).ConfigureAwait(false))
            {
                TestCase.Require(!doc.RootElement.GetProperty("filesystemMirror").GetBoolean(), "An explicit filesystemMirror false should opt out.");
            }

            using (JsonDocument doc = await ReadJsonAsync(await PostAsync(admin, ScopesPath(h.TenantId), new { name = "no-target" }).ConfigureAwait(false)).ConfigureAwait(false))
            {
                TestCase.Require(!doc.RootElement.GetProperty("filesystemMirror").GetBoolean(), "A scope with no targetPath has nowhere to mirror and should start unmirrored.");
            }

            string mirrorDir = Path.Combine(h.WorkDir, "repo", ".notdory");
            HttpResponseMessage created = await PostAsync(admin, ScopesPath(h.TenantId), new { name = "mirrored", filesystemMirror = true, targetPath = mirrorDir }).ConfigureAwait(false);
            ExpectStatus(created, HttpStatusCode.Created, "RecallDb scope with a filesystem mirror");
            Dictionary<string, object?> body = new Dictionary<string, object?>();
            string scopeId;
            using (JsonDocument doc = await ReadJsonAsync(created).ConfigureAwait(false))
            {
                TestCase.Require(doc.RootElement.GetProperty("filesystemMirror").GetBoolean(), "filesystemMirror should be stored.");
                scopeId = doc.RootElement.GetProperty("id").GetString()!;
                foreach (JsonProperty property in doc.RootElement.EnumerateObject()) body[property.Name] = property.Value.Clone();
            }

            TestCase.Require(Directory.Exists(mirrorDir), "Creating a mirrored scope should create its target directory.");

            body["targetPath"] = null;
            HttpResponseMessage clearedPath = await PutAsync(admin, ScopesPath(h.TenantId) + "/" + scopeId, body).ConfigureAwait(false);
            ExpectStatus(clearedPath, HttpStatusCode.BadRequest, "update that keeps the mirror but clears targetPath");

            body["targetPath"] = mirrorDir;
            body["filesystemMirror"] = false;
            HttpResponseMessage off = await PutAsync(admin, ScopesPath(h.TenantId) + "/" + scopeId, body).ConfigureAwait(false);
            ExpectStatus(off, HttpStatusCode.OK, "turn the mirror off");
            using (JsonDocument doc = await ReadJsonAsync(off).ConfigureAwait(false))
            {
                TestCase.Require(!doc.RootElement.GetProperty("filesystemMirror").GetBoolean(), "filesystemMirror should be off after the update.");
            }
        }

        private static async Task ScopeCreateUnknownProviderRejectedAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId), new { name = "vx", storeProvider = "Verbex" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "Unknown store provider");
        }

        private static async Task EndpointInvalidBaseUrlAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage create = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "bad", kind = "Embedding", apiFormat = "Ollama", baseUrl = "ftp://127.0.0.1/x", model = "m" }).ConfigureAwait(false);
            ExpectStatus(create, HttpStatusCode.BadRequest, "ftp base URL");
            HttpResponseMessage relative = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "bad", kind = "Embedding", apiFormat = "Ollama", baseUrl = "not a url", model = "m" }).ConfigureAwait(false);
            ExpectStatus(relative, HttpStatusCode.BadRequest, "relative base URL");
            HttpResponseMessage batch = await PostAsync(admin, EndpointsPath(h.TenantId) + "/batch", new { items = new object[] { new { name = "ok", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "m" }, new { name = "bad", kind = "Embedding", apiFormat = "Ollama", baseUrl = "nope", model = "m" } } }).ConfigureAwait(false);
            ExpectStatus(batch, HttpStatusCode.BadRequest, "batch with an invalid base URL");
        }

        private static async Task FailureRecordedAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            using HttpClient admin = h.AdminClient();
            string sid = await CreateScopeAsync(access, h, "failures").ConfigureAwait(false);
            HttpResponseMessage failed = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search", new { queryText = new string('x', 5000) }).ConfigureAwait(false);
            ExpectStatus(failed, HttpStatusCode.BadRequest, "an oversized query");
            TestCase.Require(!(await failed.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("x-notdory-exception", StringComparison.Ordinal), "The exception summary must not be returned to the caller.");

            // Post-routing records the request after the response is sent, so allow the history write a moment to land.
            List<JsonNode?> rows = new List<JsonNode?>();
            for (int attempt = 0; attempt < 20 && rows.Count == 0; attempt++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                JsonNode history = JsonNode.Parse(await (await admin.GetAsync("/v1.0/api/requests?maxResults=100").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
                rows = history["objects"]!.AsArray().Where(r => (r?["path"]?.GetValue<string>() ?? string.Empty).EndsWith(sid + "/memories/search", StringComparison.Ordinal)).ToList();
            }

            TestCase.Require(rows.Count == 1, "The failed request should be recorded exactly once, got " + rows.Count + ".");
            string headers = rows[0]?["responseHeaders"]?.GetValue<string>() ?? string.Empty;
            TestCase.Require(rows[0]?["statusCode"]?.GetValue<int>() == 400 && headers.Contains("x-notdory-exception", StringComparison.Ordinal) && headers.Contains("ArgumentOutOfRangeException", StringComparison.Ordinal), "The history row should carry the status and exception summary: " + headers);

            using HttpClient badKey = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + h.Port) };
            badKey.DefaultRequestHeaders.Add("x-access-key", "not-a-real-key");
            ExpectStatus(await badKey.GetAsync("/v1.0/api/whoami?probe=rejected").ConfigureAwait(false), HttpStatusCode.Unauthorized, "an unknown access key");
            // The 401 reaches the client before its history row is written, so allow the write a moment to land.
            int rejected = 0;
            string seen = string.Empty;
            for (int attempt = 0; attempt < 20 && rejected == 0; attempt++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                JsonNode after = JsonNode.Parse(await (await admin.GetAsync("/v1.0/api/requests?maxResults=100").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
                List<JsonNode?> matches = after["objects"]!.AsArray().Where(r => (r?["path"]?.GetValue<string>() ?? string.Empty).Contains("probe=rejected", StringComparison.Ordinal)).ToList();
                rejected = matches.Count;
                seen = string.Join(", ", matches.Select(r => r?["statusCode"]?.ToJsonString()));
            }

            TestCase.Require(rejected == 1 && seen == "401", "A request rejected during authentication should be recorded once as a 401, got " + rejected + " (" + seen + ").");

            // The deployment incident's shape: the access-key lookup throws while session requests keep working. The failure
            // must be answered by the exception mapping and recorded with its exception summary.
            await h.Database.ExecuteQueryAsync("DROP TABLE credentials;", true).ConfigureAwait(false);
            using HttpClient keyed = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + h.Port) };
            keyed.DefaultRequestHeaders.Add("x-access-key", "another-unknown-key");
            HttpResponseMessage broken = await keyed.GetAsync("/v1.0/api/whoami?probe=authfailure").ConfigureAwait(false);
            TestCase.Require((int)broken.StatusCode == 500 && (await broken.Content.ReadAsStringAsync().ConfigureAwait(false)).Contains("InternalError", StringComparison.Ordinal), "A failing credential lookup should answer 500 InternalError, got " + (int)broken.StatusCode + ".");
            string failureHeaders = string.Empty;
            for (int attempt = 0; attempt < 20 && failureHeaders.Length == 0; attempt++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                JsonNode after = JsonNode.Parse(await (await admin.GetAsync("/v1.0/api/requests?maxResults=100").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
                JsonNode? row = after["objects"]!.AsArray().FirstOrDefault(r => (r?["path"]?.GetValue<string>() ?? string.Empty).Contains("probe=authfailure", StringComparison.Ordinal));
                failureHeaders = row?["responseHeaders"]?.GetValue<string>() ?? string.Empty;
            }

            TestCase.Require(failureHeaders.Contains("x-notdory-exception", StringComparison.Ordinal) && failureHeaders.Contains("SqliteException", StringComparison.Ordinal), "A request that failed while authenticating should be recorded with its exception: " + failureHeaders);
        }

        private static async Task AgentProtocolReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anonymous = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + h.Port) };
            HttpResponseMessage r = await anonymous.GetAsync("/v1.0/api/agent-protocol").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "anonymous agent protocol read");
            JsonNode protocol = JsonNode.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(protocol["serverInstructions"]?.GetValue<string>() == NotDory.Core.Helpers.AgentProtocol.ServerInstructions && protocol["serverInstructionsOverridden"]?.GetValue<bool>() == false, "The default instructions should be reported as not overridden.");
            JsonArray tools = protocol["tools"]!.AsArray();
            TestCase.Require(tools.Count == NotDory.Core.Helpers.AgentToolCatalog.Defaults.Count && tools[0]?["name"]?.GetValue<string>() == "session_start", "Every tool should be listed, session_start first.");
            TestCase.Require(tools.All(t => t?["description"]?.GetValue<string>() == t?["defaultDescription"]?.GetValue<string>() && t?["overridden"]?.GetValue<bool>() == false), "Every description should start at its default.");
        }

        private static async Task AgentProtocolEditAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            using HttpClient admin = h.AdminClient();
            object edit = new { serverInstructions = "Custom protocol: search NotDory before every answer.", toolDescriptions = new Dictionary<string, string> { { "memory_search", "Custom search description." }, { "whoami", "" } } };

            HttpResponseMessage forbidden = await PutAsync(access, "/v1.0/api/agent-protocol", edit).ConfigureAwait(false);
            ExpectStatus(forbidden, HttpStatusCode.Forbidden, "a non-admin editing the agent protocol");
            HttpResponseMessage unknown = await PutAsync(admin, "/v1.0/api/agent-protocol", new { toolDescriptions = new Dictionary<string, string> { { "not_a_tool", "x" } } }).ConfigureAwait(false);
            ExpectStatus(unknown, HttpStatusCode.BadRequest, "an unknown tool name");

            HttpResponseMessage saved = await PutAsync(admin, "/v1.0/api/agent-protocol", edit).ConfigureAwait(false);
            ExpectStatus(saved, HttpStatusCode.OK, "an admin edit");
            JsonNode protocol = JsonNode.Parse(await saved.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(protocol["serverInstructionsOverridden"]?.GetValue<bool>() == true && protocol["serverInstructions"]?.GetValue<string>() == "Custom protocol: search NotDory before every answer.", "The instructions edit should apply.");
            JsonNode search = protocol["tools"]!.AsArray().First(t => t?["name"]?.GetValue<string>() == "memory_search")!;
            JsonNode who = protocol["tools"]!.AsArray().First(t => t?["name"]?.GetValue<string>() == "whoami")!;
            TestCase.Require(search["description"]?.GetValue<string>() == "Custom search description." && search["overridden"]?.GetValue<bool>() == true && who["overridden"]?.GetValue<bool>() == false, "A description edit should apply; a blank one keeps the default.");
            TestCase.Require(File.ReadAllText(Path.Combine(h.WorkDir, "notdory.json")).Contains("Custom protocol: search NotDory", StringComparison.Ordinal), "The edit should be saved to the settings file.");

            await CreateScopeAsync(access, h, "edited").ConfigureAwait(false);
            JsonNode session = JsonNode.Parse(await (await PostAsync(access, "/v1.0/api/session", new { project = "edited" }).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require((session["protocol"]?.GetValue<string>() ?? string.Empty).Contains("Custom protocol: search NotDory", StringComparison.Ordinal), "Session start should use the edited instructions: " + session["protocol"]);

            HttpResponseMessage reset = await PutAsync(admin, "/v1.0/api/agent-protocol", new { }).ConfigureAwait(false);
            JsonNode restored = JsonNode.Parse(await reset.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(restored["serverInstructionsOverridden"]?.GetValue<bool>() == false && restored["tools"]!.AsArray().All(t => t?["overridden"]?.GetValue<bool>() == false), "An empty body should restore every default.");
        }

        private static async Task SessionStartAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "alpha-project").ConfigureAwait(false);
            HttpResponseMessage saved = await PostAsync(access, MemoriesPath(h.TenantId, sid), new { categoryId = "decisions", slug = "db-choice", title = "Database choice", summary = "PostgreSQL for metadata.", body = "We chose PostgreSQL." }).ConfigureAwait(false);
            ExpectStatus(saved, HttpStatusCode.OK, "upsert by category name");

            HttpResponseMessage r = await PostAsync(access, "/v1.0/api/session", new { project = "Alpha Project" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "session start");
            JsonNode session = JsonNode.Parse(await r.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(session["scope"]?["id"]?.GetValue<string>() == sid && session["scope"]?["created"]?.GetValue<bool>() == false, "'Alpha Project' should match alpha-project: " + session.ToJsonString());
            TestCase.Require(session["tenantId"]?.GetValue<string>() == h.TenantId && (session["protocol"]?.GetValue<string>() ?? string.Empty).Contains(sid, StringComparison.Ordinal), "The session should carry the tenant and a protocol naming the scope.");
            TestCase.Require(session["memoryCount"]?.GetValue<long>() == 1 && session["recentMemories"]?[0]?["slug"]?.GetValue<string>() == "db-choice" && session["recentMemories"]?[0]?["category"]?.GetValue<string>() == "decisions", "The session should list the recent memory with its category name: " + session.ToJsonString());
            TestCase.Require(session["categories"]?.AsArray().Count == 1, "The session should list the scope's categories.");
            TestCase.Require(session["notice"] == null, "A scope that already has memories should not carry an onboarding notice: " + session.ToJsonString());

            HttpResponseMessage text = await access.GetAsync("/v1.0/api/session?project=alpha-project&format=text&maxMemories=5").ConfigureAwait(false);
            ExpectStatus(text, HttpStatusCode.OK, "session start as text");
            string markdown = await text.Content.ReadAsStringAsync().ConfigureAwait(false);
            TestCase.Require(markdown.StartsWith("# NotDory memory", StringComparison.Ordinal) && markdown.Contains("db-choice", StringComparison.Ordinal) && markdown.Contains("memory_search", StringComparison.Ordinal), "The text form should render the protocol and recent memories: " + markdown);
        }

        private static async Task SessionStartChoiceAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateScopeAsync(access, h, "first").ConfigureAwait(false);
            await CreateScopeAsync(access, h, "second").ConfigureAwait(false);

            JsonNode none = JsonNode.Parse(await (await PostAsync(access, "/v1.0/api/session", new { }).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(none["scope"] == null && none["scopes"]?.AsArray().Count >= 2 && none["notice"] != null, "Without a project and with several scopes, the session should list them: " + none.ToJsonString());

            JsonNode missing = JsonNode.Parse(await (await PostAsync(access, "/v1.0/api/session", new { project = "zeta", createIfMissing = false }).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(missing["scope"] == null && (missing["notice"]?.GetValue<string>() ?? string.Empty).Contains("zeta", StringComparison.Ordinal), "An unmatched project without createIfMissing should not create a scope.");

            using HttpClient admin = h.AdminClient();
            await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            JsonNode created = JsonNode.Parse(await (await PostAsync(access, "/v1.0/api/session", new { project = "brand-new" }).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(created["scope"]?["created"]?.GetValue<bool>() == true && created["scope"]?["name"]?.GetValue<string>() == "brand-new" && created["scope"]?["storeProvider"]?.GetValue<string>() == "RecallDb", "An unmatched project should get a new RecallDB scope: " + created.ToJsonString());
            TestCase.Require((created["notice"]?.GetValue<string>() ?? string.Empty).Contains("onboard", StringComparison.Ordinal), "A new scope should carry a notice to onboard the project: " + created.ToJsonString());
            JsonNode reused = JsonNode.Parse(await (await PostAsync(access, "/v1.0/api/session", new { project = "Brand New" }).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(reused["scope"]?["id"]?.GetValue<string>() == created["scope"]?["id"]?.GetValue<string>() && reused["scope"]?["created"]?.GetValue<bool>() == false, "The next session should find the scope it created.");
        }

        private static async Task SessionStartMirrorAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            using HttpClient admin = h.AdminClient();
            await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);

            string repo = Path.Combine(h.WorkDir, "repo");
            Directory.CreateDirectory(repo);
            JsonNode mirrored = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?project=repo&path=" + Uri.EscapeDataString(repo)).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            string scopeId = mirrored["scope"]?["id"]?.GetValue<string>() ?? string.Empty;
            JsonNode scope = JsonNode.Parse(await (await admin.GetAsync(ScopesPath(h.TenantId) + "/" + scopeId).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(scope["filesystemMirror"]?.GetValue<bool>() == true && scope["targetPath"]?.GetValue<string>() == repo, "A new scope should mirror under the project path: " + scope.ToJsonString());
            TestCase.Require(!(mirrored["notice"]?.GetValue<string>() ?? string.Empty).Contains("not mirrored", StringComparison.Ordinal), "A mirrored scope needs no mirror notice.");

            string unseen = Path.Combine(h.WorkDir, "elsewhere", "missing");
            JsonNode remote = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?project=remote-repo&path=" + Uri.EscapeDataString(unseen)).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require((remote["notice"]?.GetValue<string>() ?? string.Empty).Contains("cannot see the project directory", StringComparison.Ordinal), "A project the server cannot see should be explained: " + remote.ToJsonString());
            TestCase.Require(!Directory.Exists(unseen), "The server must not create a project directory it cannot see.");

            string taken = Path.Combine(h.WorkDir, "taken");
            Directory.CreateDirectory(Path.Combine(taken, ".okf"));
            File.WriteAllText(Path.Combine(taken, ".okf", "index.md"), "# Theirs\n");
            JsonNode busy = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?project=taken&path=" + Uri.EscapeDataString(taken)).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require((busy["notice"]?.GetValue<string>() ?? string.Empty).Contains("already exists and is not empty", StringComparison.Ordinal), "An existing bundle should be left alone: " + busy.ToJsonString());
            TestCase.Require(File.ReadAllText(Path.Combine(taken, ".okf", "index.md")) == "# Theirs\n", "The existing bundle's index must not be touched.");
        }

        private static async Task SessionStartRemoteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string only = await CreateScopeAsync(access, h, "NotDory").ConfigureAwait(false);

            // Nothing names a project and the tenant has one scope: use it, even though the folder name matches nothing.
            JsonNode lone = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?directory=Downloads").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(lone["scope"]?["id"]?.GetValue<string>() == only, "A folder name alone should fall back to the tenant's only scope: " + lone.ToJsonString());

            // The clone lives in a folder named differently from the repository: the remote finds the scope.
            JsonNode clone = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?directory=AgentMemory&remote=" + Uri.EscapeDataString("https://github.com/jchristn/notdory.git")).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(clone["scope"]?["id"]?.GetValue<string>() == only && clone["scope"]?["created"]?.GetValue<bool>() == false, "The remote's repository name should find the scope: " + clone.ToJsonString());

            await CreateScopeAsync(access, h, "other").ConfigureAwait(false);
            // A bare folder that matches nothing, with several scopes: no scope is created.
            JsonNode bare = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?directory=Downloads").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(bare["scope"] == null && bare["scopes"]?.AsArray().Count == 2, "A folder name alone should never create a scope: " + bare.ToJsonString());

            // A folder name that matches an existing scope finds it.
            JsonNode byFolder = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?directory=Other").ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(byFolder["scope"]?["name"]?.GetValue<string>() == "other", "A folder name should find a matching scope.");

            // A new repository gets its own scope, named for the repository.
            using HttpClient admin = h.AdminClient();
            await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "emb", kind = "Embedding", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "all-minilm", dimensionality = 384 }).ConfigureAwait(false);
            JsonNode fresh = JsonNode.Parse(await (await access.GetAsync("/v1.0/api/session?directory=work&remote=" + Uri.EscapeDataString("git@github.com:acme/new-service.git")).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false))!;
            TestCase.Require(fresh["scope"]?["name"]?.GetValue<string>() == "new-service" && fresh["scope"]?["created"]?.GetValue<bool>() == true, "A new repository should get a scope named for it: " + fresh.ToJsonString());
        }

        private static async Task MemoryUpsertCategoryNameAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "cats").ConfigureAwait(false);
            HttpResponseMessage first = await PostAsync(access, MemoriesPath(h.TenantId, sid), new { categoryId = "lessons", slug = "a", body = "First lesson." }).ConfigureAwait(false);
            ExpectStatus(first, HttpStatusCode.OK, "upsert creating a category by name");
            HttpResponseMessage second = await PostAsync(access, MemoriesPath(h.TenantId, sid), new { categoryId = "Lessons", slug = "b", body = "Second lesson." }).ConfigureAwait(false);
            ExpectStatus(second, HttpStatusCode.OK, "upsert reusing the category by name");
            TestCase.Require(await CountAsync(access, CategoriesPath(h.TenantId, sid)).ConfigureAwait(false) == 1, "The category name should create exactly one category.");
            HttpResponseMessage unknown = await PostAsync(access, MemoriesPath(h.TenantId, sid), new { categoryId = "cat_doesnotexist", slug = "c", body = "x" }).ConfigureAwait(false);
            ExpectStatus(unknown, HttpStatusCode.BadRequest, "an unknown cat_ id");
        }

        private static async Task EndpointReasoningRestAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage plain = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "plain", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "m" }).ConfigureAwait(false);
            ExpectStatus(plain, HttpStatusCode.Created, "an endpoint without a reasoning setting");
            TestCase.Require(JsonNode.Parse(await plain.Content.ReadAsStringAsync().ConfigureAwait(false))?["reasoning"]?.GetValue<string>() == "Default", "The reasoning setting should default to Default.");

            HttpResponseMessage low = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "low", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "gpt-oss:20b", reasoning = "Low" }).ConfigureAwait(false);
            ExpectStatus(low, HttpStatusCode.Created, "an endpoint with reasoning Low");
            string id = JsonNode.Parse(await low.Content.ReadAsStringAsync().ConfigureAwait(false))?["id"]?.GetValue<string>() ?? string.Empty;
            HttpResponseMessage read = await admin.GetAsync(EndpointsPath(h.TenantId) + "/" + id).ConfigureAwait(false);
            TestCase.Require(JsonNode.Parse(await read.Content.ReadAsStringAsync().ConfigureAwait(false))?["reasoning"]?.GetValue<string>() == "Low", "Reasoning Low should be stored.");

            HttpResponseMessage update = await PutAsync(admin, EndpointsPath(h.TenantId) + "/" + id, new { name = "low", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "qwen3:8b", reasoning = "Off" }).ConfigureAwait(false);
            ExpectStatus(update, HttpStatusCode.OK, "updating the reasoning setting");
            TestCase.Require(JsonNode.Parse(await update.Content.ReadAsStringAsync().ConfigureAwait(false))?["reasoning"]?.GetValue<string>() == "Off", "The update should store reasoning Off.");
        }

        private static async Task EndpointHealthCheckUrlAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage full = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "proxied", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:8900/v1.0/api/gpt-oss-20b/", model = "gpt-oss:20b", healthCheckUrl = "http://127.0.0.1:8900/" }).ConfigureAwait(false);
            ExpectStatus(full, HttpStatusCode.Created, "a full health check URL");
            string body = await full.Content.ReadAsStringAsync().ConfigureAwait(false);
            TestCase.Require(body.Contains("\"healthCheckUrl\": \"http://127.0.0.1:8900/\"", StringComparison.Ordinal) || body.Contains("\"healthCheckUrl\":\"http://127.0.0.1:8900/\"", StringComparison.Ordinal), "The full health check URL should be stored as given, got " + body);
            HttpResponseMessage path = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "pathed", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "m", healthCheckUrl = "/api/tags" }).ConfigureAwait(false);
            ExpectStatus(path, HttpStatusCode.Created, "a health check path");
            HttpResponseMessage ftp = await PostAsync(admin, EndpointsPath(h.TenantId), new { name = "bad", kind = "Inference", apiFormat = "Ollama", baseUrl = "http://127.0.0.1:11434", model = "m", healthCheckUrl = "ftp://127.0.0.1/" }).ConfigureAwait(false);
            ExpectStatus(ftp, HttpStatusCode.BadRequest, "an ftp health check URL");
        }

        private static async Task MemorySearchEmptyQueryAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, cid, "centerline", "Control the centerline.").ConfigureAwait(false);

            HttpResponseMessage missing = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search",
                new { mode = "Keyword", topK = 5 }).ConfigureAwait(false);
            ExpectStatus(missing, HttpStatusCode.BadRequest, "search without queryText");

            HttpResponseMessage blank = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search",
                new { queryText = "   ", mode = "Keyword", topK = 5 }).ConfigureAwait(false);
            ExpectStatus(blank, HttpStatusCode.BadRequest, "search with blank queryText");
        }

        private static async Task MemorySearchCategoryByNameAsync()
        {
            await MemorySearchCategoryFilterAsync(true).ConfigureAwait(false);
        }

        private static async Task MemorySearchCategoryByIdAsync()
        {
            await MemorySearchCategoryFilterAsync(false).ConfigureAwait(false);
        }

        private static async Task MemorySearchCategoryFilterAsync(bool byName)
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string notes = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            string other = await CreateCategoryAsync(access, h, sid, "other").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, notes, "in-notes", "Posture and framing in notes.").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, other, "in-other", "Posture and framing elsewhere.").ConfigureAwait(false);

            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search",
                new { queryText = "posture framing", mode = "Keyword", topK = 5, categoryFilter = byName ? "Notes" : notes }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "search with category filter");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            JsonElement hits = doc.RootElement.GetProperty("hits");
            TestCase.Require(hits.GetArrayLength() == 1, "category filter should return exactly the one memory in that category, got " + hits.GetArrayLength() + ".");
            TestCase.Require(hits[0].GetProperty("slug").GetString() == "in-notes", "category filter returned the wrong memory.");
        }

        private static async Task MemorySearchCategoryUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            await UpsertMemoryAsync(access, h, sid, cid, "centerline", "Control the centerline.").ConfigureAwait(false);

            HttpResponseMessage r = await PostAsync(access, MemoriesPath(h.TenantId, sid) + "/search",
                new { queryText = "centerline", mode = "Keyword", topK = 5, categoryFilter = "no-such-category" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "search with unknown category");
        }

        private static async Task MemoryDeleteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            string cid = await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            string mid = await UpsertMemoryAsync(access, h, sid, cid, "m1", "Body about posture and framing.").ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(MemoriesPath(h.TenantId, sid) + "/" + mid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NoContent, "delete memory");
        }

        private static async Task MemoryDeleteUnknownAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(MemoriesPath(h.TenantId, sid) + "/mem_unknown").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NotFound, "delete unknown memory");
        }

        #endregion

        #region Private-Methods-Endpoints

        private static async Task EndpointCreateEmbeddingAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, EndpointsPath(h.TenantId),
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", baseUrl = "http://127.0.0.1:9000", dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create embedding endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require((doc.RootElement.GetProperty("id").GetString() ?? string.Empty).StartsWith("eep_", StringComparison.Ordinal), "embedding endpoint id should start with 'eep_'.");
        }

        private static async Task EndpointCreateInferenceAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, EndpointsPath(h.TenantId),
                new { name = "chat", kind = "Inference", apiFormat = "OpenAI", baseUrl = "http://127.0.0.1:8080" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create inference endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require((doc.RootElement.GetProperty("id").GetString() ?? string.Empty).StartsWith("iep_", StringComparison.Ordinal), "inference endpoint id should start with 'iep_'.");
        }

        private static async Task EndpointListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(EndpointsPath(h.TenantId)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list endpoints");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() >= 1, "endpoint list should include the created endpoint.");
        }

        private static async Task EndpointListByKindAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            await CreateInferenceEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(EndpointsPath(h.TenantId) + "?kind=Embedding").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list endpoints by kind");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() == 1, "kind filter should return only the embedding endpoint.");
            foreach (JsonElement obj in doc.RootElement.GetProperty("objects").EnumerateArray())
            {
                TestCase.Require((obj.GetProperty("id").GetString() ?? string.Empty).StartsWith("eep_", StringComparison.Ordinal), "kind=Embedding results should only contain embedding endpoints.");
            }
        }

        private static async Task EndpointReadAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string eid = await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(EndpointsPath(h.TenantId) + "/" + eid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "read endpoint");
        }

        private static async Task EndpointUpdateAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string eid = await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await PutAsync(access, EndpointsPath(h.TenantId) + "/" + eid,
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", baseUrl = "http://127.0.0.1:9000", dimensionality = 512 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "update endpoint");
        }

        private static async Task EndpointDeleteAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string eid = await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await access.DeleteAsync(EndpointsPath(h.TenantId) + "/" + eid).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.NoContent, "delete endpoint");
        }

        private static async Task EndpointHealthAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            await CreateEmbeddingEndpointAsync(access, h).ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(TenantPath(h.TenantId) + "/endpoint-health").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "endpoint health");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("endpoints", out JsonElement eps) && eps.ValueKind == JsonValueKind.Array, "endpoint health should carry an 'endpoints' array.");
            TestCase.Require(doc.RootElement.TryGetProperty("probesPerformed", out _), "endpoint health should report 'probesPerformed'.");
        }

        #endregion

        #region Private-Methods-Chat

        private static async Task ChatNoEndpointAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId) + "/" + sid + "/chat", new { question = "What do you remember?" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "chat without inference endpoint");
            string text = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            TestCase.Require(text.Contains("NoInferenceEndpoint", StringComparison.Ordinal), "chat error should be NoInferenceEndpoint, got: " + text);
        }

        private static async Task ChatNoQuestionAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            HttpResponseMessage r = await PostAsync(access, ScopesPath(h.TenantId) + "/" + sid + "/chat", new { }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "chat without question");
        }

        #endregion

        #region Private-Methods-Collections

        private static async Task CollectionsNoRecallDbAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            HttpResponseMessage r = await admin.GetAsync(TenantPath(h.TenantId) + "/collections").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.BadRequest, "collections without RecallDB");
            string text = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
            TestCase.Require(text.Contains("RecallDbNotConfigured", StringComparison.Ordinal), "collections error should be RecallDbNotConfigured, got: " + text);
        }

        #endregion

        #region Private-Methods-Guide

        private static async Task GuideAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            string sid = await CreateScopeAsync(access, h, "s1").ConfigureAwait(false);
            await CreateCategoryAsync(access, h, sid, "notes").ConfigureAwait(false);
            HttpResponseMessage r = await access.GetAsync(ScopesPath(h.TenantId) + "/" + sid + "/guide").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "guide");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("categories", out JsonElement cats) && cats.ValueKind == JsonValueKind.Array, "guide should carry a 'categories' array.");
            TestCase.Require(doc.RootElement.TryGetProperty("capabilities", out _), "guide should carry 'capabilities'.");
            TestCase.Require(doc.RootElement.TryGetProperty("instructions", out _), "guide should carry 'instructions'.");
        }

        #endregion

        #region Private-Methods-RequestHistory

        private static async Task RequestsListAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();

            // Generate some captured traffic (each request is recorded during post-routing).
            await admin.GetAsync(Api + "/whoami").ConfigureAwait(false);
            await admin.GetAsync(Tenants).ConfigureAwait(false);

            HttpResponseMessage r = await admin.GetAsync(Api + "/requests?maxResults=50").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "list request history");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.GetProperty("totalRecords").GetInt64() >= 1, "request history should have captured traffic.");
            foreach (JsonElement entry in doc.RootElement.GetProperty("objects").EnumerateArray())
            {
                string path = entry.GetProperty("path").GetString() ?? string.Empty;
                TestCase.Require(!path.Contains("/api/health", StringComparison.Ordinal), "health checks should be excluded from request history.");
            }
        }

        private static async Task RequestsClearAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = h.AdminClient();
            await admin.GetAsync(Tenants).ConfigureAwait(false);
            HttpResponseMessage r = await admin.DeleteAsync(Api + "/requests").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "clear request history");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require(doc.RootElement.TryGetProperty("deleted", out _), "clear response should carry 'deleted'.");
        }

        private static async Task RequestsAnonAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient anon = h.AnonymousClient();
            HttpResponseMessage r = await anon.GetAsync(Api + "/requests").ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Unauthorized, "anon list request history");
        }

        #endregion

        #region Private-Methods-Paths

        private const string Api = "/v1.0/api";
        private const string Tenants = Api + "/tenants";

        private static string TenantPath(string tenantId) => Tenants + "/" + tenantId;
        private static string ScopesPath(string tenantId) => TenantPath(tenantId) + "/scopes";
        private static string CategoriesPath(string tenantId, string scopeId) => ScopesPath(tenantId) + "/" + scopeId + "/categories";
        private static string MemoriesPath(string tenantId, string scopeId) => ScopesPath(tenantId) + "/" + scopeId + "/memories";
        private static string EndpointsPath(string tenantId) => TenantPath(tenantId) + "/endpoints";

        #endregion

        #region Private-Methods-Helpers

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static object NewScope(ServerHarness h, string name)
        {
            return new { name, storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = Path.Combine(h.WorkDir, name) };
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
        {
            StringContent content = new StringContent(JsonSerializer.Serialize(body, _Json), Encoding.UTF8, "application/json");
            return await client.PostAsync(path, content).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body)
        {
            StringContent content = new StringContent(JsonSerializer.Serialize(body, _Json), Encoding.UTF8, "application/json");
            return await client.PutAsync(path, content).ConfigureAwait(false);
        }

        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        {
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        }

        private static void ExpectStatus(HttpResponseMessage response, HttpStatusCode expected, string label)
        {
            TestCase.Require(response.StatusCode == expected, label + ": expected " + expected + " but got " + response.StatusCode + ".");
        }

        private static async Task<string> CreateTenantAsync(HttpClient admin, string name)
        {
            HttpResponseMessage r = await PostAsync(admin, Tenants, new { name }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create tenant");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("tenant").GetProperty("id").GetString() ?? throw new InvalidOperationException("No tenant id.");
        }

        private static async Task<string> CreateScopeAsync(HttpClient client, ServerHarness h, string name)
        {
            HttpResponseMessage r = await PostAsync(client, ScopesPath(h.TenantId), NewScope(h, name)).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create scope");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No scope id.");
        }

        private static async Task<string> CreateCategoryAsync(HttpClient client, ServerHarness h, string scopeId, string name)
        {
            HttpResponseMessage r = await PostAsync(client, CategoriesPath(h.TenantId, scopeId), new { name, instructions = "One idea per memory." }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create category");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No category id.");
        }

        private static async Task<string> UpsertMemoryAsync(HttpClient client, ServerHarness h, string scopeId, string categoryId, string slug, string body)
        {
            HttpResponseMessage r = await PostAsync(client, MemoriesPath(h.TenantId, scopeId), new { categoryId, slug, title = slug, body }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.OK, "setup upsert memory");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No memory id.");
        }

        private static async Task<string> CreateEmbeddingEndpointAsync(HttpClient client, ServerHarness h)
        {
            HttpResponseMessage r = await PostAsync(client, EndpointsPath(h.TenantId),
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", baseUrl = "http://127.0.0.1:9000", dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create embedding endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No endpoint id.");
        }

        private static async Task<string> CreateInferenceEndpointAsync(HttpClient client, ServerHarness h)
        {
            HttpResponseMessage r = await PostAsync(client, EndpointsPath(h.TenantId),
                new { name = "chat", kind = "Inference", apiFormat = "OpenAI", baseUrl = "http://127.0.0.1:8080" }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create inference endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No endpoint id.");
        }

        #endregion
    }
}
