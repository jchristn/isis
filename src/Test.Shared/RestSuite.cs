namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end REST test suite for the Isis server. Each case boots a real in-process Isis REST server
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
                "Isis REST Suite",
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

                    // Scopes.
                    TestCase.Async("rest", "scope-create", "POST /scopes as a credential creates a scope", ScopeCreateAsync),
                    TestCase.Async("rest", "scope-create-dup", "POST /scopes with a duplicate name conflicts", ScopeCreateDuplicateAsync),
                    TestCase.Async("rest", "scope-create-noname", "POST /scopes without a name is a bad request", ScopeCreateNoNameAsync),
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
            TestCase.Require(!string.IsNullOrEmpty(doc.RootElement.GetProperty("id").GetString()), "created tenant should have an id.");
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
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", hostname = "127.0.0.1", port = 9000, dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "create embedding endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            TestCase.Require((doc.RootElement.GetProperty("id").GetString() ?? string.Empty).StartsWith("eep_", StringComparison.Ordinal), "embedding endpoint id should start with 'eep_'.");
        }

        private static async Task EndpointCreateInferenceAsync()
        {
            using ServerHarness h = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient access = h.AccessClient();
            HttpResponseMessage r = await PostAsync(access, EndpointsPath(h.TenantId),
                new { name = "chat", kind = "Inference", apiFormat = "OpenAI", hostname = "127.0.0.1", port = 8080 }).ConfigureAwait(false);
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
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", hostname = "127.0.0.1", port = 9000, dimensionality = 512 }).ConfigureAwait(false);
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
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No tenant id.");
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
                new { name = "embed", kind = "Embedding", apiFormat = "OpenAI", hostname = "127.0.0.1", port = 9000, dimensionality = 384 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create embedding endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No endpoint id.");
        }

        private static async Task<string> CreateInferenceEndpointAsync(HttpClient client, ServerHarness h)
        {
            HttpResponseMessage r = await PostAsync(client, EndpointsPath(h.TenantId),
                new { name = "chat", kind = "Inference", apiFormat = "OpenAI", hostname = "127.0.0.1", port = 8080 }).ConfigureAwait(false);
            ExpectStatus(r, HttpStatusCode.Created, "setup create inference endpoint");
            using JsonDocument doc = await ReadJsonAsync(r).ConfigureAwait(false);
            return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No endpoint id.");
        }

        #endregion
    }
}
