namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Health;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.McpServer;
    using Isis.McpServer.Settings;
    using Isis.Server.Services;
    using Microsoft.Data.Sqlite;
    using Touchstone.Core;

    /// <summary>
    /// Automated Isis test suites.
    /// </summary>
    public static class IsisSuites
    {
        #region Public-Methods

        /// <summary>
        /// Get the shared Touchstone test suites.
        /// </summary>
        /// <returns>Test suite descriptors.</returns>
        public static IReadOnlyList<TestSuiteDescriptor> GetSuites()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(
                    "isis",
                    "Isis Shared Suite",
                    new List<TestCaseDescriptor>
                    {
                        Async("sqlite-round-trip", "SQLite round trip", SqliteRoundTripAsync),
                        Async("filesystem-store-search", "Filesystem store search", FilesystemStoreSearchAsync),
                        Async("memory-service-idempotent", "Memory upsert is idempotent by slug", MemoryServiceIdempotentAsync),
                        Async("tenant-isolation", "Tenant isolation on enumeration", TenantIsolationAsync),
                        Sync("store-capabilities", "Store capability descriptors", StoreCapabilities),
                        Async("http-end-to-end", "HTTP end to end (memory lifecycle)", HttpEndToEndAsync),
                        Async("http-auth-and-isolation", "HTTP authentication and tenant isolation", HttpAuthAndIsolationAsync),
                        Async("http-request-history", "Request history captures traffic", HttpRequestHistoryAsync),
                        Async("http-collections-passthrough", "Collections route reports RecallDB not configured", HttpCollectionsPassthroughAsync),
                        Async("mcp-proxy-end-to-end", "MCP server proxies REST end to end", McpProxyEndToEndAsync),
                        Async("http-chat-requires-endpoint", "Chat route requires a configured inference endpoint", HttpChatRequiresEndpointAsync),
                        Async("endpoint-crud", "Model endpoint persistence round trip", EndpointCrudAsync),
                        Async("healthcheck-dedup", "Health check deduplicates by method and URL", HealthCheckDedupAsync),
                        Async("embedding-service", "Embedding service parses a vector", EmbeddingServiceAsync),
                        Async("chat-with-memory", "Chat with memory retrieves, grounds, and cites", ChatWithMemoryAsync),
                        Skippable("live-postgresql", "Live PostgreSQL round trip (ephemeral container)", LivePostgresqlAsync, !DockerDb.Available(), "Docker is not available."),
                        Skippable("live-mysql", "Live MySQL round trip (ephemeral container)", LiveMysqlAsync, !DockerDb.Available(), "Docker is not available."),
                        Skippable("live-sqlserver", "Live SQL Server round trip (ephemeral container)", LiveSqlServerAsync, !DockerDb.Available(), "Docker is not available.")
                    }),
                AuthSuite.Suite(),
                ModelSuite.Suite(),
                DatabaseSuite.Suite(),
                StoreSuite.Suite(),
                ServiceSuite.Suite(),
                RestSuite.Suite(),
                McpSuite.Suite(),
                InstallSuite.Suite()
            };
        }

        #endregion

        #region Private-Methods-Descriptors

        private static TestCaseDescriptor Async(string caseId, string displayName, Func<Task> executeAsync)
        {
            return new TestCaseDescriptor(
                "isis",
                caseId,
                displayName,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await executeAsync().ConfigureAwait(false);
                },
                new[] { "isis" });
        }

        private static TestCaseDescriptor Skippable(string caseId, string displayName, Func<Task> executeAsync, bool skip, string skipReason)
        {
            return new TestCaseDescriptor(
                "isis",
                caseId,
                displayName,
                async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await executeAsync().ConfigureAwait(false);
                },
                new[] { "isis", "live" })
            {
                Skip = skip,
                SkipReason = skip ? skipReason : null
            };
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action execute)
        {
            return new TestCaseDescriptor(
                "isis",
                caseId,
                displayName,
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    execute();
                    return Task.CompletedTask;
                },
                new[] { "isis" });
        }

        #endregion

        #region Private-Methods-Direct

        private static async Task SqliteRoundTripAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-rt-" + Guid.NewGuid().ToString("N") + ".db");
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            try
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
                Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
                Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);
                Memory memory = new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "s1", Title = "T", Body = "B" };
                memory.Tags.Add("x");
                memory.Metadata["k"] = "v";
                await db.Memories.CreateAsync(memory).ConfigureAwait(false);

                Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, "s1").ConfigureAwait(false);
                if (read == null) throw new InvalidOperationException("Expected memory by slug.");
                if (read.Tags.Count != 1 || read.Tags[0] != "x") throw new InvalidOperationException("Tags did not round trip.");
                if (!read.Metadata.ContainsKey("k") || read.Metadata["k"] != "v") throw new InvalidOperationException("Metadata did not round trip.");

                Scope? byName = await db.Scopes.ReadByNameAsync(tenant.Id, "proj").ConfigureAwait(false);
                if (byName == null || byName.Id != scope.Id) throw new InvalidOperationException("Scope by name failed.");
            }
            finally
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                TryDelete(file);
            }
        }

        private static async Task FilesystemStoreSearchAsync()
        {
            string work = Path.Combine(Path.GetTempPath(), "isis-fs-" + Guid.NewGuid().ToString("N"));
            Scope scope = new Scope { TenantId = "ten_x", Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.Hierarchy, TargetPath = work };
            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                Memory memory = new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = "cat_1", Slug = "centerline", Title = "Centerline", Body = "Control the centerline; posture and framing win positions." };
                string key = await store.UpsertAsync(scope, memory, null).ConfigureAwait(false);
                if (string.IsNullOrEmpty(key) || !File.Exists(key)) throw new InvalidOperationException("Expected a written memory file.");

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "posture framing", Mode = SearchModeEnum.Hybrid, TopK = 5 }, null).ConfigureAwait(false);
                if (result.EffectiveMode != SearchModeEnum.Keyword) throw new InvalidOperationException("Filesystem store should serve keyword search.");
                if (string.IsNullOrEmpty(result.Notice)) throw new InvalidOperationException("Expected a degradation notice for a hybrid request.");
                if (result.Hits.Count < 1) throw new InvalidOperationException("Expected at least one hit.");
                if (result.Hits[0].Slug != "centerline") throw new InvalidOperationException("Expected the centerline hit.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task MemoryServiceIdempotentAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-idem-" + Guid.NewGuid().ToString("N") + ".db");
            string work = Path.Combine(Path.GetTempPath(), "isis-idem-" + Guid.NewGuid().ToString("N"));
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            try
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
                Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = work }).ConfigureAwait(false);
                Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);
                MemoryService service = new MemoryService(db);

                Memory first = await service.UpsertAsync(scope, category, new Memory { Slug = "layout", Title = "V1", Body = "first" }).ConfigureAwait(false);
                Memory second = await service.UpsertAsync(scope, category, new Memory { Slug = "layout", Title = "V2", Body = "second" }).ConfigureAwait(false);

                if (first.Id != second.Id) throw new InvalidOperationException("Upsert by slug must reuse the same id.");
                if (second.Version != first.Version + 1) throw new InvalidOperationException("Expected version to increment on update.");

                EnumerationResult<Memory> all = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, null, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
                if (all.TotalRecords != 1) throw new InvalidOperationException("Expected exactly one memory row after two upserts, found " + all.TotalRecords + ".");
                if (second.Title != "V2" || second.Body != "second") throw new InvalidOperationException("Expected the latest content to win.");
            }
            finally
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                TryDelete(file);
                TryDeleteDir(work);
            }
        }

        private static async Task TenantIsolationAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-iso-" + Guid.NewGuid().ToString("N") + ".db");
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            try
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Tenant a = await db.Tenants.CreateAsync(new Tenant { Name = "A" }).ConfigureAwait(false);
                Tenant b = await db.Tenants.CreateAsync(new Tenant { Name = "B" }).ConfigureAwait(false);
                Scope sa = await db.Scopes.CreateAsync(new Scope { TenantId = a.Id, Name = "sa" }).ConfigureAwait(false);
                Scope sb = await db.Scopes.CreateAsync(new Scope { TenantId = b.Id, Name = "sb" }).ConfigureAwait(false);

                EnumerationResult<Scope> aScopes = await db.Scopes.EnumerateAsync(a.Id, new EnumerationQuery { MaxResults = 100 }).ConfigureAwait(false);
                if (aScopes.TotalRecords != 1 || aScopes.Objects[0].Id != sa.Id) throw new InvalidOperationException("Tenant A should see only its own scope.");

                Scope? leak = await db.Scopes.ReadAsync(a.Id, sb.Id).ConfigureAwait(false);
                if (leak != null) throw new InvalidOperationException("Cross-tenant scope read must return null.");
            }
            finally
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                TryDelete(file);
            }
        }

        private static void StoreCapabilities()
        {
            IMemoryStore recall = MemoryStoreFactory.Create(StoreProviderEnum.RecallDb);
            if (!recall.Capabilities.SupportsSemantic || !recall.Capabilities.SupportsHybrid || !recall.Capabilities.RequiresEmbedding) throw new InvalidOperationException("RecallDB must advertise semantic + hybrid + embedding.");

            IMemoryStore verbex = MemoryStoreFactory.Create(StoreProviderEnum.Verbex);
            if (verbex.Capabilities.SupportsSemantic || verbex.Capabilities.SupportsHybrid) throw new InvalidOperationException("Verbex must not advertise semantic or hybrid.");
            if (!verbex.Capabilities.SupportsKeyword) throw new InvalidOperationException("Verbex must advertise keyword search.");

            IMemoryStore fs = MemoryStoreFactory.Create(StoreProviderEnum.Filesystem);
            if (fs.Capabilities.SupportsSemantic || fs.Capabilities.RequiresEmbedding) throw new InvalidOperationException("Filesystem must not advertise semantic or embeddings.");
        }

        private static async Task EndpointCrudAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-ep-" + Guid.NewGuid().ToString("N") + ".db");
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            try
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);

                ModelEndpoint embedding = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
                {
                    TenantId = tenant.Id,
                    Name = "local-embed",
                    Kind = EndpointKindEnum.Embedding,
                    ApiFormat = ApiFormatEnum.Ollama,
                    Hostname = "127.0.0.1",
                    Port = 11434,
                    Model = "nomic-embed-text",
                    Dimensionality = 768
                }).ConfigureAwait(false);
                if (!embedding.Id.StartsWith("eep_", StringComparison.Ordinal)) throw new InvalidOperationException("Embedding endpoint id should use the eep_ prefix.");

                ModelEndpoint inference = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
                {
                    TenantId = tenant.Id,
                    Name = "local-chat",
                    Kind = EndpointKindEnum.Inference,
                    ApiFormat = ApiFormatEnum.OpenAI,
                    Hostname = "127.0.0.1",
                    Port = 8080
                }).ConfigureAwait(false);
                if (!inference.Id.StartsWith("iep_", StringComparison.Ordinal)) throw new InvalidOperationException("Inference endpoint id should use the iep_ prefix.");

                ModelEndpoint? read = await db.ModelEndpoints.ReadAsync(tenant.Id, embedding.Id).ConfigureAwait(false);
                if (read == null || read.Dimensionality != 768 || read.Model != "nomic-embed-text") throw new InvalidOperationException("Embedding endpoint did not round trip.");

                EnumerationResult<ModelEndpoint> embeddings = await db.ModelEndpoints.EnumerateAsync(tenant.Id, EndpointKindEnum.Embedding, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
                if (embeddings.TotalRecords != 1) throw new InvalidOperationException("Expected one embedding endpoint via kind filter.");

                read.Dimensionality = 1024;
                await db.ModelEndpoints.UpdateAsync(read).ConfigureAwait(false);
                ModelEndpoint? updated = await db.ModelEndpoints.ReadAsync(tenant.Id, embedding.Id).ConfigureAwait(false);
                if (updated == null || updated.Dimensionality != 1024) throw new InvalidOperationException("Endpoint update did not persist.");

                if (!await db.ModelEndpoints.DeleteAsync(tenant.Id, inference.Id).ConfigureAwait(false)) throw new InvalidOperationException("Expected endpoint delete to succeed.");
            }
            finally
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                TryDelete(file);
            }
        }

        private static async Task HealthCheckDedupAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };

            if (HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b)) throw new InvalidOperationException("Endpoints with the same method/URL/auth must share a dedup key.");

            int probes = await service.ProbeOnceAsync(new[] { a, b }).ConfigureAwait(false);
            if (probes != 1) throw new InvalidOperationException("Two endpoints with the same URL must be probed once, got " + probes + ".");
            if (handler.Count != 1) throw new InvalidOperationException("The HTTP endpoint should have been hit once, got " + handler.Count + ".");

            ModelEndpoint c = new ModelEndpoint { TenantId = "t", Name = "c", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/other" };
            if (HealthCheckService.BuildKey(a) == HealthCheckService.BuildKey(c)) throw new InvalidOperationException("Endpoints with different paths must not share a dedup key.");

            handler.Reset();
            int probes2 = await service.ProbeOnceAsync(new[] { a, c }).ConfigureAwait(false);
            if (probes2 != 2) throw new InvalidOperationException("Two endpoints with different URLs must be probed twice, got " + probes2 + ".");
            if (handler.Count != 2) throw new InvalidOperationException("The HTTP endpoints should have been hit twice, got " + handler.Count + ".");

            // Endpoint 'a' succeeded in both rounds; with the default healthy threshold of 2 it should now be healthy.
            EndpointHealthStatus? statusA = service.GetStatus(a.Id);
            if (statusA == null || !statusA.IsHealthy) throw new InvalidOperationException("Endpoint 'a' should be healthy after two successful probes.");

            // An endpoint whose expected status never matches becomes unhealthy after the unhealthy threshold.
            ModelEndpoint bad = new ModelEndpoint { TenantId = "t", Name = "bad", Hostname = "127.0.0.1", Port = 9001, HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 599 };
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);
            await service.ProbeOnceAsync(new[] { bad }).ConfigureAwait(false);
            EndpointHealthStatus? statusBad = service.GetStatus(bad.Id);
            if (statusBad == null || statusBad.IsHealthy) throw new InvalidOperationException("Endpoint 'bad' should be unhealthy after failing probes.");
        }

        private static async Task EmbeddingServiceAsync()
        {
            string body = JsonSerializer.Serialize(new { data = new[] { new { embedding = new[] { 0.1, 0.2, 0.3, 0.4 } } } });
            using HttpClient client = new HttpClient(new StubResponseHandler(body));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9998 };

            float[] vector = await service.EmbedAsync(endpoint, "hello world").ConfigureAwait(false);
            if (vector.Length != 4) throw new InvalidOperationException("Expected a 4-dimensional vector, got " + vector.Length + ".");
            if (Math.Abs(vector[0] - 0.1f) > 0.0001) throw new InvalidOperationException("Vector element 0 was not parsed correctly.");
        }

        private static async Task ChatWithMemoryAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-chat-" + Guid.NewGuid().ToString("N") + ".db");
            string work = Path.Combine(Path.GetTempPath(), "isis-chat-" + Guid.NewGuid().ToString("N"));
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            try
            {
                await db.InitializeAsync().ConfigureAwait(false);
                Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
                Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "book", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = work }).ConfigureAwait(false);
                Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes" }).ConfigureAwait(false);

                MemoryService memoryService = new MemoryService(db);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "grip", Title = "Grip fighting", Body = "Win the grip to win the exchange; control the sleeve and collar." }).ConfigureAwait(false);

                string completion = JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { role = "assistant", content = "Win the grip first, controlling the sleeve and collar [grip]." } } }
                });
                using HttpClient client = new HttpClient(new StubResponseHandler(completion));
                InferenceService inference = new InferenceService(client);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = tenant.Id, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "How do I win the exchange?", 5).ConfigureAwait(false);

                if (!answer.Answer.Contains("grip", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Expected the synthesized answer to mention the grip.");
                if (answer.Citations.Count < 1 || answer.Citations[0].Slug != "grip") throw new InvalidOperationException("Expected a citation to the 'grip' memory.");
                if (answer.RetrievalMode != SearchModeEnum.Keyword) throw new InvalidOperationException("Filesystem retrieval should report keyword mode.");
            }
            finally
            {
                db.Dispose();
                SqliteConnection.ClearAllPools();
                TryDelete(file);
                TryDeleteDir(work);
            }
        }

        #endregion

        #region Private-Methods-LiveDb

        private static async Task LivePostgresqlAsync()
        {
            int port = DockerDb.FreePort();
            string name = "isis-test-pg-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            ProcessResult run = DockerDb.Run("run -d --rm --name " + name + " -e POSTGRES_DB=isis -e POSTGRES_USER=isis -e POSTGRES_PASSWORD=isis -p 127.0.0.1:" + port + ":5432 ankane/pgvector:v0.5.1", 180000);
            if (run.ExitCode != 0) throw new InvalidOperationException("Unable to start PostgreSQL container: " + run.Error);

            try
            {
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Postgresql, Hostname = "127.0.0.1", Port = port, DatabaseName = "isis", Username = "isis", Password = "isis" };
                if (!await DockerDb.WaitForPingAsync(() => DatabaseDriverFactory.Create(settings), 60, 1000).ConfigureAwait(false)) throw new InvalidOperationException("PostgreSQL did not become ready.");
                using DatabaseDriverBase db = DatabaseDriverFactory.Create(settings);
                await DbRoundTripAsync(db).ConfigureAwait(false);
            }
            finally
            {
                DockerDb.Run("rm -f " + name, 30000);
            }
        }

        private static async Task LiveMysqlAsync()
        {
            int port = DockerDb.FreePort();
            string name = "isis-test-mysql-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            ProcessResult run = DockerDb.Run("run -d --rm --name " + name + " -e MYSQL_DATABASE=isis -e MYSQL_USER=isis -e MYSQL_PASSWORD=isis -e MYSQL_ROOT_PASSWORD=isisroot -p 127.0.0.1:" + port + ":3306 mysql:8.4", 240000);
            if (run.ExitCode != 0) throw new InvalidOperationException("Unable to start MySQL container: " + run.Error);

            try
            {
                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.Mysql, Hostname = "127.0.0.1", Port = port, DatabaseName = "isis", Username = "isis", Password = "isis" };
                if (!await DockerDb.WaitForPingAsync(() => DatabaseDriverFactory.Create(settings), 90, 2000).ConfigureAwait(false)) throw new InvalidOperationException("MySQL did not become ready.");
                using DatabaseDriverBase db = DatabaseDriverFactory.Create(settings);
                await DbRoundTripAsync(db).ConfigureAwait(false);
            }
            finally
            {
                DockerDb.Run("rm -f " + name, 30000);
            }
        }

        private static async Task LiveSqlServerAsync()
        {
            int port = DockerDb.FreePort();
            string name = "isis-test-mssql-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            ProcessResult run = DockerDb.Run("run -d --rm --name " + name + " -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=Isis_Str0ng! -p 127.0.0.1:" + port + ":1433 mcr.microsoft.com/mssql/server:2022-latest", 300000);
            if (run.ExitCode != 0) throw new InvalidOperationException("Unable to start SQL Server container: " + run.Error);

            try
            {
                DatabaseSettings master = new DatabaseSettings { Type = DatabaseTypeEnum.SqlServer, Hostname = "127.0.0.1", Port = port, DatabaseName = "master", Username = "sa", Password = "Isis_Str0ng!" };
                if (!await DockerDb.WaitForPingAsync(() => DatabaseDriverFactory.Create(master), 90, 2000).ConfigureAwait(false)) throw new InvalidOperationException("SQL Server did not become ready.");

                using (DatabaseDriverBase masterDriver = DatabaseDriverFactory.Create(master))
                {
                    await masterDriver.ExecuteQueryAsync("IF DB_ID('isis') IS NULL CREATE DATABASE isis;", true).ConfigureAwait(false);
                }

                DatabaseSettings settings = new DatabaseSettings { Type = DatabaseTypeEnum.SqlServer, Hostname = "127.0.0.1", Port = port, DatabaseName = "isis", Username = "sa", Password = "Isis_Str0ng!" };
                using DatabaseDriverBase db = DatabaseDriverFactory.Create(settings);
                await DbRoundTripAsync(db).ConfigureAwait(false);
            }
            finally
            {
                DockerDb.Run("rm -f " + name, 30000);
            }
        }

        private static async Task DbRoundTripAsync(DatabaseDriverBase db)
        {
            await db.InitializeAsync().ConfigureAwait(false);

            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Tenant? tenantRead = await db.Tenants.ReadAsync(tenant.Id).ConfigureAwait(false);
            if (tenantRead == null || tenantRead.Name != "Acme") throw new InvalidOperationException("Tenant did not round trip.");

            Scope scope = await db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
            Category category = await db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = scope.Id, Name = "notes", Instructions = "one idea per memory" }).ConfigureAwait(false);

            for (int i = 1; i <= 3; i++)
            {
                Memory memory = new Memory { TenantId = tenant.Id, ScopeId = scope.Id, CategoryId = category.Id, Slug = "m" + i, Title = "T" + i, Body = "body " + i };
                memory.Tags.Add("tag" + i);
                memory.Metadata["k"] = "v" + i;
                await db.Memories.CreateAsync(memory).ConfigureAwait(false);
            }

            Memory? read = await db.Memories.ReadBySlugAsync(tenant.Id, scope.Id, category.Id, "m2").ConfigureAwait(false);
            if (read == null || read.Title != "T2") throw new InvalidOperationException("Memory did not round trip by slug.");
            if (read.Tags.Count != 1 || read.Tags[0] != "tag2") throw new InvalidOperationException("Memory tags did not round trip.");
            if (!read.Metadata.TryGetValue("k", out string? kv) || kv != "v2") throw new InvalidOperationException("Memory metadata did not round trip.");

            // Pagination exercises the provider-specific pagination clause (LIMIT/OFFSET vs OFFSET/FETCH).
            EnumerationResult<Memory> page1 = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, null, new EnumerationQuery { MaxResults = 2, Skip = 0 }).ConfigureAwait(false);
            if (page1.TotalRecords != 3 || page1.Objects.Count != 2) throw new InvalidOperationException("Pagination page 1 wrong: total=" + page1.TotalRecords + " count=" + page1.Objects.Count + ".");
            EnumerationResult<Memory> page2 = await db.Memories.EnumerateAsync(tenant.Id, scope.Id, null, new EnumerationQuery { MaxResults = 2, Skip = 2 }).ConfigureAwait(false);
            if (page2.Objects.Count != 1) throw new InvalidOperationException("Pagination page 2 wrong: count=" + page2.Objects.Count + ".");

            ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { TenantId = tenant.Id, Name = "e", Kind = EndpointKindEnum.Embedding, Hostname = "127.0.0.1", Port = 1234, Dimensionality = 384 }).ConfigureAwait(false);
            if (!endpoint.Id.StartsWith("eep_", StringComparison.Ordinal)) throw new InvalidOperationException("Endpoint id prefix wrong.");
            EnumerationResult<ModelEndpoint> embeddings = await db.ModelEndpoints.EnumerateAsync(tenant.Id, EndpointKindEnum.Embedding, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
            if (embeddings.TotalRecords != 1) throw new InvalidOperationException("Endpoint kind filter wrong.");

            await db.RequestHistory.CreateAsync(new RequestHistoryEntry { TenantId = tenant.Id, Method = "GET", Path = "/x", StatusCode = 200, DurationMs = 1.5 }).ConfigureAwait(false);
            EnumerationResult<RequestHistoryEntry> history = await db.RequestHistory.EnumerateAsync(tenant.Id, new EnumerationQuery { MaxResults = 10 }).ConfigureAwait(false);
            if (history.TotalRecords < 1) throw new InvalidOperationException("Request history did not persist.");

            Scope? leak = await db.Scopes.ReadAsync("ten_does_not_exist", scope.Id).ConfigureAwait(false);
            if (leak != null) throw new InvalidOperationException("Cross-tenant scope read must return null.");
        }

        #endregion

        #region Private-Methods-Http

        private static async Task HttpEndToEndAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = harness.AdminClient();

            HttpResponseMessage health = await admin.GetAsync("/v1.0/api/health").ConfigureAwait(false);
            Expect(health, HttpStatusCode.OK, "health");

            string scopeId;
            using (JsonDocument scope = await PostAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes",
                new { name = "proj", storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = Path.Combine(harness.WorkDir, "mem") }, HttpStatusCode.Created, "create scope").ConfigureAwait(false))
            {
                scopeId = scope.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No scope id.");
            }

            string categoryId;
            using (JsonDocument category = await PostAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/categories",
                new { name = "notes", instructions = "One memory per idea." }, HttpStatusCode.Created, "create category").ConfigureAwait(false))
            {
                categoryId = category.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No category id.");
            }

            using (JsonDocument memory = await PostAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/memories",
                new { slug = "centerline", categoryId = categoryId, title = "The Centerline", body = "Control the centerline; posture and framing win positions.", type = "Project" }, HttpStatusCode.OK, "upsert memory").ConfigureAwait(false))
            {
                if (memory.RootElement.GetProperty("slug").GetString() != "centerline") throw new InvalidOperationException("Memory slug mismatch.");
                if (string.IsNullOrEmpty(memory.RootElement.GetProperty("storeKey").GetString())) throw new InvalidOperationException("Expected a store key on the persisted memory.");
            }

            using (JsonDocument guide = await GetAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/guide", HttpStatusCode.OK, "guide").ConfigureAwait(false))
            {
                JsonElement categories = guide.RootElement.GetProperty("categories");
                if (categories.GetArrayLength() < 1) throw new InvalidOperationException("Guide should list the category.");
                if (categories[0].GetProperty("instructions").GetString() != "One memory per idea.") throw new InvalidOperationException("Guide should include category instructions.");
            }

            using (JsonDocument search = await PostAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/memories/search",
                new { queryText = "posture framing", mode = "Keyword", topK = 5 }, HttpStatusCode.OK, "search").ConfigureAwait(false))
            {
                JsonElement hits = search.RootElement.GetProperty("hits");
                if (hits.GetArrayLength() < 1) throw new InvalidOperationException("Search should return at least one hit.");
                if (hits[0].GetProperty("slug").GetString() != "centerline") throw new InvalidOperationException("Search hit slug mismatch.");
            }

            using (JsonDocument openApi = await GetAsync(admin, "/openapi.json", HttpStatusCode.OK, "openapi").ConfigureAwait(false))
            {
                if (!openApi.RootElement.TryGetProperty("paths", out JsonElement paths)) throw new InvalidOperationException("OpenAPI document has no paths.");
                if (!paths.TryGetProperty("/v1.0/api/tenants", out JsonElement _)) throw new InvalidOperationException("OpenAPI should document /v1.0/api/tenants.");
            }
        }

        private static async Task HttpRequestHistoryAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = harness.AdminClient();

            // Generate some traffic (each completes its own capture during PostRouting).
            await admin.GetAsync("/v1.0/api/whoami").ConfigureAwait(false);
            await admin.GetAsync("/v1.0/api/tenants").ConfigureAwait(false);
            await admin.GetAsync("/v1.0/api/server/info").ConfigureAwait(false);

            using JsonDocument history = await GetAsync(admin, "/v1.0/api/requests?maxResults=50", HttpStatusCode.OK, "list request history").ConfigureAwait(false);
            long total = history.RootElement.GetProperty("totalRecords").GetInt64();
            if (total < 2) throw new InvalidOperationException("Expected request history to capture prior traffic, got " + total + ".");

            bool sawTenants = false;
            foreach (JsonElement entry in history.RootElement.GetProperty("objects").EnumerateArray())
            {
                string path = entry.GetProperty("path").GetString() ?? string.Empty;
                if (path.Contains("/v1.0/api/tenants", StringComparison.Ordinal)) sawTenants = true;
                if (path.Contains("/api/health", StringComparison.Ordinal)) throw new InvalidOperationException("Health checks should be excluded from request history.");
            }

            if (!sawTenants) throw new InvalidOperationException("Expected the tenants request to appear in history.");
        }

        private static async Task HttpCollectionsPassthroughAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = harness.AdminClient();

            // The harness configures no RecallDB endpoint, so the pass-through should report it clearly (route is wired).
            HttpResponseMessage response = await admin.GetAsync("/v1.0/api/tenants/" + harness.TenantId + "/collections").ConfigureAwait(false);
            Expect(response, HttpStatusCode.BadRequest, "collections without RecallDB");
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!text.Contains("RecallDbNotConfigured", StringComparison.Ordinal)) throw new InvalidOperationException("Expected RecallDbNotConfigured, got: " + text);
        }

        private static async Task HttpChatRequiresEndpointAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);
            using HttpClient admin = harness.AdminClient();

            string scopeId;
            using (JsonDocument scope = await PostAsync(admin, "/v1.0/api/tenants/" + harness.TenantId + "/scopes",
                new { name = "chatscope", storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = Path.Combine(harness.WorkDir, "chatmem") }, HttpStatusCode.Created, "create scope").ConfigureAwait(false))
            {
                scopeId = scope.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("No scope id.");
            }

            // With no inference endpoint configured, the chat route should report a clear 400.
            StringContent content = new StringContent(JsonSerializer.Serialize(new { question = "What do you remember?" }, _Json), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await admin.PostAsync("/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/chat", content).ConfigureAwait(false);
            Expect(response, HttpStatusCode.BadRequest, "chat without inference endpoint");
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!text.Contains("NoInferenceEndpoint", StringComparison.Ordinal)) throw new InvalidOperationException("Expected a NoInferenceEndpoint error, got: " + text);
        }

        private static async Task HttpAuthAndIsolationAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);

            using (HttpClient anon = harness.AnonymousClient())
            {
                HttpResponseMessage unauthorized = await anon.GetAsync("/v1.0/api/tenants").ConfigureAwait(false);
                Expect(unauthorized, HttpStatusCode.Unauthorized, "anonymous list tenants");
            }

            using (HttpClient access = harness.AccessClient())
            {
                HttpResponseMessage own = await access.GetAsync("/v1.0/api/tenants/" + harness.TenantId + "/scopes").ConfigureAwait(false);
                Expect(own, HttpStatusCode.OK, "credential lists own tenant scopes");

                HttpResponseMessage cross = await access.GetAsync("/v1.0/api/tenants/ten_someone_else/scopes").ConfigureAwait(false);
                Expect(cross, HttpStatusCode.Forbidden, "credential blocked from another tenant");
            }
        }

        private static async Task McpProxyEndToEndAsync()
        {
            using ServerHarness harness = await ServerHarness.StartAsync().ConfigureAwait(false);

            McpServerSettings settings = new McpServerSettings
            {
                Hostname = "127.0.0.1",
                Port = GetFreePort(),
                RestHostname = "127.0.0.1",
                RestPort = harness.Port
            };

            using CancellationTokenSource cts = new CancellationTokenSource();
            using IsisMcpServer mcp = new IsisMcpServer(settings);
            mcp.Start(cts.Token);
            await WaitForMcpAsync(settings.Port).ConfigureAwait(false);

            McpCallerCredentials admin = new McpCallerCredentials { AccessKey = harness.AccessKey, SecretKey = harness.SecretKey };
            McpCallerCredentials access = new McpCallerCredentials { AccessKey = harness.AccessKey, SecretKey = harness.SecretKey };

            // whoami through the proxy, authenticated as the tenant credential.
            JsonElement who = Data(await mcp.ProxyAsync(HttpMethod.Get, "/v1.0/api/whoami", null, "isis_whoami", access).ConfigureAwait(false), "whoami");
            if (who.GetProperty("tenantId").GetString() != harness.TenantId) throw new InvalidOperationException("Expected whoami to resolve the default tenant.");

            // create a scope as admin.
            string scopeBody = JsonSerializer.Serialize(new { name = "mcpproj", storeProvider = "Filesystem", filesystemLayout = "Hierarchy", targetPath = Path.Combine(harness.WorkDir, "mcpmem") });
            JsonElement scope = Data(await mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/" + harness.TenantId + "/scopes", scopeBody, "isis_scope_create", admin).ConfigureAwait(false), "create scope");
            string scopeId = scope.GetProperty("id").GetString() ?? throw new InvalidOperationException("No scope id.");

            // create a category.
            string categoryBody = JsonSerializer.Serialize(new { name = "notes", instructions = "One idea per memory." });
            JsonElement category = Data(await mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/categories", categoryBody, "isis_category_create", admin).ConfigureAwait(false), "create category");
            string categoryId = category.GetProperty("id").GetString() ?? throw new InvalidOperationException("No category id.");

            // upsert a memory.
            string memoryBody = JsonSerializer.Serialize(new { categoryId = categoryId, slug = "grip", title = "Grip fighting", body = "Win the grip to win the exchange; control the sleeve and collar." });
            JsonElement memory = Data(await mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/memories", memoryBody, "isis_memory_upsert", access).ConfigureAwait(false), "upsert memory");
            if (memory.GetProperty("slug").GetString() != "grip") throw new InvalidOperationException("Expected upserted slug 'grip'.");

            // search through the proxy.
            string searchBody = JsonSerializer.Serialize(new { queryText = "grip collar", mode = "Keyword", topK = 5 });
            JsonElement search = Data(await mcp.ProxyAsync(HttpMethod.Post, "/v1.0/api/tenants/" + harness.TenantId + "/scopes/" + scopeId + "/memories/search", searchBody, "isis_memory_search", access).ConfigureAwait(false), "search");
            if (search.GetProperty("hits").GetArrayLength() < 1) throw new InvalidOperationException("Expected at least one MCP search hit.");
        }

        #endregion

        #region Private-Methods-Helpers

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static async Task<JsonDocument> PostAsync(HttpClient client, string path, object body, HttpStatusCode expected, string label)
        {
            StringContent content = new StringContent(JsonSerializer.Serialize(body, _Json), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(path, content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != expected) throw new InvalidOperationException(label + ": expected " + expected + " but got " + response.StatusCode + " (" + text + ")");
            return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        }

        private static async Task<JsonDocument> GetAsync(HttpClient client, string path, HttpStatusCode expected, string label)
        {
            HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode != expected) throw new InvalidOperationException(label + ": expected " + expected + " but got " + response.StatusCode + " (" + text + ")");
            return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
        }

        private static void Expect(HttpResponseMessage response, HttpStatusCode expected, string label)
        {
            if (response.StatusCode != expected) throw new InvalidOperationException(label + ": expected " + expected + " but got " + response.StatusCode + ".");
        }

        private static JsonElement Data(object envelopeObject, string label)
        {
            if (envelopeObject is not Dictionary<string, object?> envelope) throw new InvalidOperationException(label + ": unexpected envelope type.");
            bool success = envelope.TryGetValue("success", out object? successValue) && successValue is bool flag && flag;
            object? statusValue = envelope.TryGetValue("statusCode", out object? sc) ? sc : null;
            if (!success) throw new InvalidOperationException(label + ": proxy call failed (status " + statusValue + ").");
            if (!envelope.TryGetValue("data", out object? dataValue) || dataValue is not JsonElement data) throw new InvalidOperationException(label + ": no data in envelope.");
            return data;
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

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        #endregion
    }
}
