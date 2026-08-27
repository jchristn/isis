namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Health;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
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
                    TestCase.Async("service", "buildkey-gemini-openai-auth-differ", "BuildKey: Gemini vs OpenAI auth headers differ", BuildKeyGeminiVsOpenAiAuthAsync),

                    // HealthCheckService probing
                    TestCase.Async("service", "probe-dedup-same-url", "Probe: shared URL is probed once", ProbeDedupSameUrlAsync),
                    TestCase.Async("service", "probe-distinct-urls", "Probe: distinct URLs are probed separately", ProbeDistinctUrlsAsync),
                    TestCase.Async("service", "probe-status-null-before", "Probe: status is null before any probe", ProbeStatusNullBeforeAsync),
                    TestCase.Async("service", "probe-healthy-after-threshold", "Probe: healthy after meeting threshold", ProbeHealthyAfterThresholdAsync),
                    TestCase.Async("service", "probe-not-healthy-before-threshold", "Probe: not healthy before threshold", ProbeNotHealthyBeforeThresholdAsync),
                    TestCase.Async("service", "probe-failure-unhealthy", "Probe: unexpected status becomes unhealthy", ProbeFailureUnhealthyAsync),
                    TestCase.Async("service", "probe-inactive-skipped", "Probe: inactive endpoints are skipped", ProbeInactiveSkippedAsync),
                    TestCase.Async("service", "snapshot-reflects-probed", "Snapshot reflects probed endpoints", SnapshotReflectsProbedAsync),

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

                    // MemoryChatService
                    TestCase.Async("service", "chat-answer-with-citations", "Chat: grounded answer cites the memory", ChatAnswerWithCitationsAsync),
                    TestCase.Async("service", "chat-no-match-notice", "Chat: no match still answers with a notice", ChatNoMatchNoticeAsync)
                });
        }

        #endregion

        #region Private-Methods-BuildKey

        private static Task BuildKeyIdenticalEqualAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            TestCase.Require(HealthCheckService.BuildKey(a) == HealthCheckService.BuildKey(b), "Identical endpoints must share a dedup key.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyPathDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/other" };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "Different health-check paths must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyMethodDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.GET };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckMethod = HealthCheckMethodEnum.HEAD };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "GET and HEAD must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyPortDiffersAsync()
        {
            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9001, HealthCheckUrl = "/health" };
            TestCase.Require(HealthCheckService.BuildKey(a) != HealthCheckService.BuildKey(b), "Different ports must produce different keys.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyAuthDiffersAsync()
        {
            ModelEndpoint noAuth = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint withAuth = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckUseAuth = true, ApiKey = "secret-key" };
            TestCase.Require(HealthCheckService.BuildKey(noAuth) != HealthCheckService.BuildKey(withAuth), "Authenticated probe must differ from the anonymous one.");
            return Task.CompletedTask;
        }

        private static Task BuildKeyGeminiVsOpenAiAuthAsync()
        {
            ModelEndpoint gemini = new ModelEndpoint { TenantId = "t", Name = "g", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckUseAuth = true, ApiKey = "k", ApiFormat = ApiFormatEnum.Gemini };
            ModelEndpoint openai = new ModelEndpoint { TenantId = "t", Name = "o", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckUseAuth = true, ApiKey = "k", ApiFormat = ApiFormatEnum.OpenAI };
            TestCase.Require(HealthCheckService.BuildKey(gemini) != HealthCheckService.BuildKey(openai), "Gemini and OpenAI auth headers must produce different keys.");
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods-Probe

        private static async Task ProbeDedupSameUrlAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };

            int probes = await service.ProbeOnceAsync(new[] { a, b }).ConfigureAwait(false);
            TestCase.Require(probes == 1, "Two endpoints sharing a URL must be probed once, got " + probes + ".");
            TestCase.Require(handler.Count == 1, "The HTTP endpoint should be hit once, got " + handler.Count + ".");
        }

        private static async Task ProbeDistinctUrlsAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint c = new ModelEndpoint { TenantId = "t", Name = "c", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/other" };

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

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            TestCase.Require(service.GetStatus(a.Id) == null, "Status must be null before any probe.");
            return Task.CompletedTask;
        }

        private static async Task ProbeHealthyAfterThresholdAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 2 };

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

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 200, HealthyThreshold = 2 };

            await service.ProbeOnceAsync(new[] { a }).ConfigureAwait(false);

            EndpointHealthStatus? status = service.GetStatus(a.Id);
            TestCase.Require(status != null, "Status must exist after a single probe.");
            TestCase.Require(!status!.IsHealthy, "Endpoint must not be healthy before the healthy threshold is met.");
        }

        private static async Task ProbeFailureUnhealthyAsync()
        {
            CountingHandler handler = new CountingHandler(HttpStatusCode.OK);
            using HttpClient client = new HttpClient(handler);
            HealthCheckService service = new HealthCheckService(client);

            ModelEndpoint bad = new ModelEndpoint { TenantId = "t", Name = "bad", Hostname = "127.0.0.1", Port = 9001, HealthCheckUrl = "/health", HealthCheckExpectedStatusCode = 599 };

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

            ModelEndpoint inactive = new ModelEndpoint { TenantId = "t", Name = "off", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health", Active = false };

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

            ModelEndpoint a = new ModelEndpoint { TenantId = "t", Name = "a", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/health" };
            ModelEndpoint b = new ModelEndpoint { TenantId = "t", Name = "b", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/other" };
            ModelEndpoint inactive = new ModelEndpoint { TenantId = "t", Name = "off", Hostname = "127.0.0.1", Port = 9000, HealthCheckUrl = "/skip", Active = false };

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
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9998 };

            float[] vector = await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false);
            TestCase.Require(vector.Length == 3, "Expected a length-3 vector, got " + vector.Length + ".");
            TestCase.Require(Math.Abs(vector[0] - 0.1f) < 0.0001f, "Vector element 0 was not parsed correctly.");
        }

        private static async Task EmbedOllamaParseAsync()
        {
            string json = JsonSerializer.Serialize(new { embedding = new[] { 0.1, 0.2 } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.Ollama, Hostname = "127.0.0.1", Port = 11434 };

            float[] vector = await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false);
            TestCase.Require(vector.Length == 2, "Expected a length-2 vector, got " + vector.Length + ".");
        }

        private static async Task EmbedErrorStatusAsync()
        {
            using HttpClient client = new HttpClient(new StubResponseHandler("{}", HttpStatusCode.InternalServerError));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9998 };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false),
                "An error status must throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static async Task EmbedMissingDataAsync()
        {
            using HttpClient client = new HttpClient(new StubResponseHandler("{\"nope\":1}"));
            EmbeddingService service = new EmbeddingService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "e", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9998 };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.EmbedAsync(endpoint, "hello").ConfigureAwait(false),
                "A response missing the data array must throw InvalidOperationException.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Inference

        private static async Task InferOpenAiAsync()
        {
            string json = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "hi" } } } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            InferenceService service = new InferenceService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

            string content = await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false);
            TestCase.Require(content == "hi", "Expected the OpenAI content 'hi', got '" + content + "'.");
        }

        private static async Task InferOllamaAsync()
        {
            string json = JsonSerializer.Serialize(new { message = new { role = "assistant", content = "yo" } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            InferenceService service = new InferenceService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.Ollama, Hostname = "127.0.0.1", Port = 11434 };

            string content = await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false);
            TestCase.Require(content == "yo", "Expected the Ollama content 'yo', got '" + content + "'.");
        }

        private static async Task InferErrorStatusAsync()
        {
            using HttpClient client = new HttpClient(new StubResponseHandler("{}", HttpStatusCode.InternalServerError));
            InferenceService service = new InferenceService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

            await TestCase.ThrowsAsync<InvalidOperationException>(
                async () => await service.CompleteAsync(endpoint, "sys", "user").ConfigureAwait(false),
                "An error status must throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static async Task InferMissingContentAsync()
        {
            string json = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant" } } } });
            using HttpClient client = new HttpClient(new StubResponseHandler(json));
            InferenceService service = new InferenceService(client);
            ModelEndpoint endpoint = new ModelEndpoint { TenantId = "t", Name = "c", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

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
                using HttpClient client = new HttpClient(new StubResponseHandler(chatJson));
                InferenceService inference = new InferenceService(client);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

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

        private static async Task ChatNoMatchNoticeAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            string work = NewWork();
            try
            {
                (Scope scope, Category category, MemoryService memoryService) = await SetupMemoryAsync(t.Db, work).ConfigureAwait(false);
                await memoryService.UpsertAsync(scope, category, new Memory { Slug = "a", Title = "Centerline", Body = "Control the centerline; posture and framing win positions." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "Answer [a]" } } } });
                using HttpClient client = new HttpClient(new StubResponseHandler(chatJson));
                InferenceService inference = new InferenceService(client);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, Hostname = "127.0.0.1", Port = 9999 };

                MemoryChatService chat = new MemoryChatService(memoryService, inference);
                ChatAnswer answer = await chat.AskAsync(scope, endpoint, "zxqwv unrelated xylophone plumbago", 5).ConfigureAwait(false);

                TestCase.Require(answer.Citations.Count == 0, "A non-matching question must yield no citations.");
                TestCase.Require(!string.IsNullOrEmpty(answer.Notice), "A non-matching question must carry a notice.");
                TestCase.Require(!string.IsNullOrEmpty(answer.Answer), "Inference is still invoked, so an answer must be returned.");
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
