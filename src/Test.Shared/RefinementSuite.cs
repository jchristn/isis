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
    using Isis.Core.Database.Migrations;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.Server.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the round-3 retrieval work in RETRIEVAL_IMPROVEMENTS.md: memory supersession, link
    /// expansion, result diversity, reranking with a relevance cutoff, the similarity report on upsert, and the lookup
    /// cache.
    /// </summary>
    public static class RefinementSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the refinement test suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "refinement",
                "Isis Retrieval Refinement Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Sync("refinement", "query-new-defaults", "MemorySearchQuery: new options default off, clamp, and survive Clone", QueryNewDefaults),
                    TestCase.Sync("refinement", "scope-rerank-defaults", "Scope: rerank settings default to none, 20 candidates, no minimum, and validate", ScopeRerankDefaults),
                    TestCase.Async("refinement", "scope-rerank-round-trip", "Scope rerank settings persist through the database", ScopeRerankRoundTripAsync),
                    TestCase.Async("refinement", "rerank-endpoint-prefix", "A Rerank endpoint gets a rep_ id", RerankEndpointPrefixAsync),
                    TestCase.Async("refinement", "migration-005-adds-columns", "Migration 005 adds the supersession columns to an older memories table", Migration005AddsColumnsAsync),
                    TestCase.Async("refinement", "migration-006-adds-columns", "Migration 006 adds the rerank columns to an older scopes table", Migration006AddsColumnsAsync),
                    TestCase.Async("refinement", "supersedes-marks-target", "Upsert with supersedes marks the named memory as replaced", SupersedesMarksTargetAsync),
                    TestCase.Async("refinement", "supersedes-reverse-order", "A memory written after its replacement is marked replaced on create", SupersedesReverseOrderAsync),
                    TestCase.Async("refinement", "supersedes-released-on-update", "Removing a slug from supersedes marks that memory current again", SupersedesReleasedOnUpdateAsync),
                    TestCase.Async("refinement", "supersedes-released-on-delete", "Deleting the replacement marks the replaced memory current again", SupersedesReleasedOnDeleteAsync),
                    TestCase.Async("refinement", "supersedes-ignores-self", "A memory cannot supersede itself and duplicate entries collapse", SupersedesIgnoresSelfAsync),
                    TestCase.Async("refinement", "search-superseded-demote", "Search (Demote): the replacement ranks first and the replaced hit is marked", SearchSupersededDemoteAsync),
                    TestCase.Async("refinement", "search-superseded-adds-replacement", "Search (Demote): a replacement the search missed is added before the replaced hit", SearchSupersededAddsReplacementAsync),
                    TestCase.Async("refinement", "search-superseded-hide", "Search (Hide): replaced memories are dropped", SearchSupersededHideAsync),
                    TestCase.Async("refinement", "search-superseded-include", "Search (Include): order is unchanged and the replaced hit is marked", SearchSupersededIncludeAsync),
                    TestCase.Async("refinement", "search-superseded-chain", "Search follows a replacement chain to the current memory", SearchSupersededChainAsync),
                    TestCase.Async("refinement", "search-link-expansion", "Search: link expansion adds linked memories (links and [[slug]]) after the linker", SearchLinkExpansionAsync),
                    TestCase.Async("refinement", "search-link-expansion-off", "Search: link expansion 0 adds nothing", SearchLinkExpansionOffAsync),
                    TestCase.Sync("refinement", "diversify-zero-keeps-order", "Diversity 0 keeps the relevance order", DiversifyZeroKeepsOrder),
                    TestCase.Sync("refinement", "diversify-demotes-near-duplicate", "Diversity moves a near-duplicate below a distinct result", DiversifyDemotesNearDuplicate),
                    TestCase.Sync("refinement", "diversify-validates", "Diversify rejects out-of-range arguments", DiversifyValidates),
                    TestCase.Async("refinement", "rerank-tei-request-and-order", "RerankService (Tei): posts query and texts to /rerank and maps scores back to input order", RerankTeiAsync),
                    TestCase.Async("refinement", "rerank-cohere-request-and-order", "RerankService (Cohere): posts to /v1/rerank and reads relevance_score", RerankCohereAsync),
                    TestCase.Async("refinement", "rerank-unsupported-format", "RerankService rejects formats without a rerank API", RerankUnsupportedFormatAsync),
                    TestCase.Async("refinement", "rerank-error-status", "RerankService surfaces an endpoint error", RerankErrorStatusAsync),
                    TestCase.Async("refinement", "search-rerank-reorders", "Search: a scope with a rerank endpoint reorders hits by rerank score", SearchRerankReordersAsync),
                    TestCase.Async("refinement", "search-rerank-cutoff", "Search: minRerankScore (query or scope) drops weak reranked hits", SearchRerankCutoffAsync),
                    TestCase.Async("refinement", "search-rerank-opt-out", "Search: rerank false skips the reranker; rerank true without an endpoint fails", SearchRerankOptOutAsync),
                    TestCase.Async("refinement", "search-rerank-outage-degrades", "Search: a failing reranker falls back to retrieval order with a notice", SearchRerankOutageDegradesAsync),
                    TestCase.Async("refinement", "upsert-no-similar-without-semantic", "Upsert: stores without semantic search report no similar memories", UpsertNoSimilarWithoutSemanticAsync),
                    TestCase.Sync("refinement", "similar-memories-omitted-when-null", "Memory JSON omits similarMemories when there are none", SimilarMemoriesOmittedWhenNull),
                    TestCase.Async("refinement", "ttl-cache-hit-and-expiry", "TtlCache serves within its time to live and reloads after", TtlCacheHitAndExpiryAsync),
                    TestCase.Async("refinement", "ttl-cache-invalidation-wins", "TtlCache: a load that raced an invalidation is not cached", TtlCacheInvalidationWinsAsync),
                    TestCase.Async("refinement", "lookup-cache-invalidate-for-write", "LookupCache: writes to scopes invalidate cached scopes; reads and memory writes do not", LookupCacheInvalidateForWriteAsync),
                    TestCase.Async("refinement", "lookup-cache-disabled", "LookupCache: disabled reads the database every time", LookupCacheDisabledAsync)
                });
        }

        #endregion

        #region Private-Methods-Fixtures

        private static async Task<FilesystemFixture> FixtureAsync(TempSqlite t, RerankService? rerank = null)
        {
            FilesystemFixture fixture = new FilesystemFixture();
            fixture.Work = Path.Combine(Path.GetTempPath(), "isis-ref-" + Guid.NewGuid().ToString("N"));
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            fixture.Scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = fixture.Work }).ConfigureAwait(false);
            fixture.Category = await t.Db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = fixture.Scope.Id, Name = "notes" }).ConfigureAwait(false);
            fixture.Service = new MemoryService(t.Db, null, null, null, rerank);
            return fixture;
        }

        private static void DeleteWork(string work)
        {
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (IOException)
            {
            }
        }

        private static Task<Memory> PutAsync(FilesystemFixture f, string slug, string body, params string[] supersedes)
        {
            return f.Service.UpsertAsync(f.Scope, f.Category, new Memory { Slug = slug, Title = slug, Body = body, Supersedes = supersedes.ToList() });
        }

        private static async Task<Memory> ReadSlugAsync(TempSqlite t, FilesystemFixture f, string slug)
        {
            Memory? memory = await t.Db.Memories.ReadBySlugAsync(f.Scope.TenantId, f.Scope.Id, f.Category.Id, slug).ConfigureAwait(false);
            return memory ?? throw new InvalidOperationException("Memory '" + slug + "' not found.");
        }

        private static Task<MemorySearchResult> KeywordAsync(FilesystemFixture f, string text, SupersededHandlingEnum handling = SupersededHandlingEnum.Demote, int linkExpansion = 0)
        {
            return f.Service.SearchAsync(f.Scope, new MemorySearchQuery { QueryText = text, Mode = SearchModeEnum.Keyword, Superseded = handling, LinkExpansion = linkExpansion });
        }

        private static string Slugs(MemorySearchResult result)
        {
            return string.Join(",", result.Hits.Select(h => h.Slug));
        }

        private static MemorySearchHit Hit(string slug, double score, string snippet)
        {
            return new MemorySearchHit { Slug = slug, StoreKey = slug, Score = score, Snippet = snippet };
        }

        private static ModelEndpoint RerankEndpoint(ApiFormatEnum format)
        {
            return new ModelEndpoint { TenantId = "ten_x", Name = "rerank", Kind = EndpointKindEnum.Rerank, ApiFormat = format, BaseUrl = "http://127.0.0.1:9", Model = "ms-marco" };
        }

        #endregion

        #region Private-Methods-Models

        private static void QueryNewDefaults()
        {
            MemorySearchQuery query = new MemorySearchQuery();
            TestCase.Require(query.Superseded == SupersededHandlingEnum.Demote, "Superseded should default to Demote.");
            TestCase.Require(query.LinkExpansion == 0 && query.Diversity == 0.0, "LinkExpansion and Diversity should default to 0.");
            TestCase.Require(query.Rerank == null && query.MinRerankScore == null, "Rerank and MinRerankScore should default to null.");

            query.LinkExpansion = 50;
            TestCase.Require(query.LinkExpansion == 10, "LinkExpansion should clamp to 10.");
            query.LinkExpansion = -1;
            TestCase.Require(query.LinkExpansion == 0, "LinkExpansion should clamp to 0.");
            query.Diversity = 2.0;
            TestCase.Require(query.Diversity == 1.0, "Diversity should clamp to 1.");

            query.QueryText = "q";
            query.TopK = 7;
            query.Diversity = 0.3;
            query.MinRerankScore = 0.4;
            query.Superseded = SupersededHandlingEnum.Hide;
            MemorySearchQuery copy = query.Clone();
            TestCase.Require(!ReferenceEquals(copy, query), "Clone should return a new instance.");
            TestCase.Require(copy.QueryText == "q" && copy.TopK == 7 && copy.Diversity == 0.3 && copy.MinRerankScore == 0.4 && copy.Superseded == SupersededHandlingEnum.Hide, "Clone should copy every setting.");
            copy.TopK = 3;
            TestCase.Require(query.TopK == 7, "Changing the clone should not change the original.");
        }

        private static void ScopeRerankDefaults()
        {
            Scope scope = new Scope();
            TestCase.Require(scope.RerankEndpointId == null && scope.RerankCandidates == 20 && scope.RerankMinScore == null, "Scope rerank settings should default to none, 20, null.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => scope.RerankCandidates = 0, "RerankCandidates 0 should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => scope.RerankCandidates = 101, "RerankCandidates above 100 should be rejected.");
        }

        private static async Task ScopeRerankRoundTripAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "p", StoreProvider = StoreProviderEnum.Filesystem, RerankEndpointId = "rep_abc", RerankCandidates = 30, RerankMinScore = 0.25 }).ConfigureAwait(false);
            Scope? read = await t.Db.Scopes.ReadAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read.RerankEndpointId == "rep_abc" && read.RerankCandidates == 30 && read.RerankMinScore == 0.25, "Created rerank settings should read back.");

            read!.RerankEndpointId = null;
            read.RerankMinScore = null;
            read.RerankCandidates = 5;
            await t.Db.Scopes.UpdateAsync(read).ConfigureAwait(false);
            Scope? updated = await t.Db.Scopes.ReadAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(updated != null && updated.RerankEndpointId == null && updated.RerankCandidates == 5 && updated.RerankMinScore == null, "Updated rerank settings should read back.");
        }

        private static async Task RerankEndpointPrefixAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            ModelEndpoint endpoint = RerankEndpoint(ApiFormatEnum.Tei);
            endpoint.TenantId = tenant.Id;
            ModelEndpoint created = await t.Db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
            TestCase.Require(created.Id.StartsWith("rep_", StringComparison.Ordinal), "A Rerank endpoint id should start with rep_, got " + created.Id + ".");
            ModelEndpoint? read = await t.Db.ModelEndpoints.ReadAsync(tenant.Id, created.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read.Kind == EndpointKindEnum.Rerank && read.ApiFormat == ApiFormatEnum.Tei, "The Rerank kind and Tei format should persist.");
            TestCase.Require(IdGenerator.Endpoint(EndpointKindEnum.Rerank).StartsWith("rep_", StringComparison.Ordinal), "IdGenerator.Endpoint(Rerank) should use rep_.");
        }

        private static async Task Migration005AddsColumnsAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("ALTER TABLE memories DROP COLUMN supersedes;", true).ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("ALTER TABLE memories DROP COLUMN supersededby;", true).ConfigureAwait(false);

            Migration005MemorySupersession migration = new Migration005MemorySupersession();
            await migration.ApplyAsync(t.Db, _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);
            await migration.ApplyAsync(t.Db, _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("SELECT supersedes, supersededby FROM memories WHERE 1 = 0;", false).ConfigureAwait(false);
        }

        private static async Task Migration006AddsColumnsAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("ALTER TABLE scopes DROP COLUMN rerankendpointid;", true).ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("ALTER TABLE scopes DROP COLUMN rerankcandidates;", true).ConfigureAwait(false);
            await t.Db.ExecuteQueryAsync("ALTER TABLE scopes DROP COLUMN rerankminscore;", true).ConfigureAwait(false);

            Migration006ScopeRerank migration = new Migration006ScopeRerank();
            await migration.ApplyAsync(t.Db, _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);
            await migration.ApplyAsync(t.Db, _ => Task.CompletedTask, CancellationToken.None).ConfigureAwait(false);

            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "p", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
            Scope? read = await t.Db.Scopes.ReadAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read.RerankCandidates == 20, "A scope written after migration 006 should read back with the default candidates.");
        }

        #endregion

        #region Private-Methods-Supersession

        private static async Task SupersedesMarksTargetAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "old", "The cache TTL is 15 minutes.").ConfigureAwait(false);
                Memory replacement = await PutAsync(f, "new", "The cache TTL is 5 minutes with jitter.", "old").ConfigureAwait(false);

                Memory old = await ReadSlugAsync(t, f, "old").ConfigureAwait(false);
                TestCase.Require(old.SupersededBy == replacement.Id, "The old memory should be marked superseded by the new one.");
                Memory current = await ReadSlugAsync(t, f, "new").ConfigureAwait(false);
                TestCase.Require(current.Supersedes.SequenceEqual(new[] { "old" }) && current.SupersededBy == null, "The new memory should persist its supersedes list and stay current.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SupersedesReverseOrderAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                Memory replacement = await PutAsync(f, "new", "The cache TTL is 5 minutes with jitter.", "old").ConfigureAwait(false);
                Memory old = await PutAsync(f, "old", "The cache TTL is 15 minutes.").ConfigureAwait(false);
                TestCase.Require(old.SupersededBy == replacement.Id, "The upsert response should show the late memory as superseded.");
                Memory read = await ReadSlugAsync(t, f, "old").ConfigureAwait(false);
                TestCase.Require(read.SupersededBy == replacement.Id, "The late memory should be persisted as superseded.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SupersedesReleasedOnUpdateAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "old", "The cache TTL is 15 minutes.").ConfigureAwait(false);
                await PutAsync(f, "new", "The cache TTL is 5 minutes.", "old").ConfigureAwait(false);
                await PutAsync(f, "new", "The cache TTL is 5 minutes, revised.").ConfigureAwait(false);

                Memory old = await ReadSlugAsync(t, f, "old").ConfigureAwait(false);
                TestCase.Require(old.SupersededBy == null, "Dropping the slug from supersedes should make the old memory current again.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SupersedesReleasedOnDeleteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "old", "The cache TTL is 15 minutes.").ConfigureAwait(false);
                Memory replacement = await PutAsync(f, "new", "The cache TTL is 5 minutes.", "old").ConfigureAwait(false);
                await f.Service.DeleteAsync(f.Scope, replacement).ConfigureAwait(false);

                Memory old = await ReadSlugAsync(t, f, "old").ConfigureAwait(false);
                TestCase.Require(old.SupersededBy == null, "Deleting the replacement should make the old memory current again.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SupersedesIgnoresSelfAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "old", "The cache TTL is 15 minutes.").ConfigureAwait(false);
                Memory saved = await PutAsync(f, "new", "Body.", "new", " old ", "old", "").ConfigureAwait(false);
                TestCase.Require(saved.Supersedes.SequenceEqual(new[] { "old" }), "Supersedes should drop the memory's own slug, blanks, and duplicates; got [" + string.Join(",", saved.Supersedes) + "].");
                TestCase.Require(saved.SupersededBy == null, "A memory must never be marked as superseding itself.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task<FilesystemFixture> TtlPairAsync(TempSqlite t)
        {
            // "old" repeats the query terms, so keyword search ranks it above its replacement.
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            await PutAsync(f, "old", "Rate quote cache TTL: the rate quote cache TTL is 15 minutes (fifteen).").ConfigureAwait(false);
            await PutAsync(f, "new", "The rate quote cache TTL is 5 minutes with jitter.", "old").ConfigureAwait(false);
            return f;
        }

        private static async Task SearchSupersededDemoteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await TtlPairAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult include = await KeywordAsync(f, "rate quote cache TTL", SupersededHandlingEnum.Include).ConfigureAwait(false);
                TestCase.Require(Slugs(include) == "old,new", "Precondition: keyword search should rank old above new, got " + Slugs(include) + ".");

                MemorySearchResult demote = await KeywordAsync(f, "rate quote cache TTL").ConfigureAwait(false);
                TestCase.Require(Slugs(demote) == "new,old", "Demote should rank the replacement first, got " + Slugs(demote) + ".");
                TestCase.Require(demote.Hits[1].SupersededBy == "new" && demote.Hits[0].SupersededBy == null, "The replaced hit should carry supersededBy.");
                TestCase.Require(demote.Hits.All(h => !string.IsNullOrEmpty(h.MemoryId)), "Every hit should be resolved to its memory id.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchSupersededAddsReplacementAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await TtlPairAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await KeywordAsync(f, "fifteen").ConfigureAwait(false);
                TestCase.Require(Slugs(result) == "new,old", "The replacement should be added before the only hit, got " + Slugs(result) + ".");
                TestCase.Require(result.Hits[0].LinkedFrom == "old" && result.Hits[0].Snippet.Contains("5 minutes", StringComparison.Ordinal), "The added replacement should carry linkedFrom and its body.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchSupersededHideAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await TtlPairAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await KeywordAsync(f, "rate quote cache TTL", SupersededHandlingEnum.Hide).ConfigureAwait(false);
                TestCase.Require(Slugs(result) == "new", "Hide should drop the replaced memory, got " + Slugs(result) + ".");
                MemorySearchResult missing = await KeywordAsync(f, "fifteen", SupersededHandlingEnum.Hide).ConfigureAwait(false);
                TestCase.Require(Slugs(missing) == "new", "Hide should still surface the replacement of a dropped hit, got " + Slugs(missing) + ".");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchSupersededIncludeAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await TtlPairAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await KeywordAsync(f, "rate quote cache TTL", SupersededHandlingEnum.Include).ConfigureAwait(false);
                TestCase.Require(Slugs(result) == "old,new", "Include should keep the retrieval order, got " + Slugs(result) + ".");
                TestCase.Require(result.Hits[0].SupersededBy == "new", "Include should still mark the replaced hit.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchSupersededChainAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "v1", "Deploys run on Jenkins (legacy pipeline) jenkins jenkins.").ConfigureAwait(false);
                await PutAsync(f, "v2", "Deploys moved to Argo CD.", "v1").ConfigureAwait(false);
                await PutAsync(f, "v3", "Deploys use Argo Rollouts canaries.", "v2").ConfigureAwait(false);

                MemorySearchResult result = await KeywordAsync(f, "jenkins").ConfigureAwait(false);
                TestCase.Require(result.Hits.Count >= 2 && result.Hits[0].Slug == "v3", "The chain should resolve to the current memory v3, got " + Slugs(result) + ".");
                TestCase.Require(result.Hits.First(h => h.Slug == "v1").SupersededBy == "v3", "The replaced hit should name the current memory.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        #endregion

        #region Private-Methods-Links

        private static async Task<FilesystemFixture> LinkedAsync(TempSqlite t)
        {
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            await f.Service.UpsertAsync(f.Scope, f.Category, new Memory { Slug = "hub", Title = "Hub", Body = "Primary runbook. See [[detail-b]] for the rollback steps.", Links = new List<string> { "detail-a" } }).ConfigureAwait(false);
            await PutAsync(f, "detail-a", "Paging goes to the on-call rotation.").ConfigureAwait(false);
            await PutAsync(f, "detail-b", "Rollback with the previous image tag.").ConfigureAwait(false);
            await PutAsync(f, "unrelated", "Office plants are watered on Fridays.").ConfigureAwait(false);
            return f;
        }

        private static async Task SearchLinkExpansionAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await LinkedAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult two = await KeywordAsync(f, "primary", SupersededHandlingEnum.Demote, 2).ConfigureAwait(false);
                TestCase.Require(Slugs(two) == "hub,detail-a,detail-b", "Link expansion 2 should add the links list then the [[slug]] reference, got " + Slugs(two) + ".");
                TestCase.Require(two.Hits[1].LinkedFrom == "hub" && two.Hits[2].LinkedFrom == "hub" && two.Hits[0].LinkedFrom == null, "Linked hits should carry linkedFrom.");

                MemorySearchResult one = await KeywordAsync(f, "primary", SupersededHandlingEnum.Demote, 1).ConfigureAwait(false);
                TestCase.Require(Slugs(one) == "hub,detail-a", "Link expansion 1 should add one linked memory, got " + Slugs(one) + ".");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchLinkExpansionOffAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await LinkedAsync(t).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await KeywordAsync(f, "primary").ConfigureAwait(false);
                TestCase.Require(Slugs(result) == "hub", "Without link expansion only the match should return, got " + Slugs(result) + ".");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        #endregion

        #region Private-Methods-Diversity

        private static List<MemorySearchHit> DiversityHits()
        {
            return new List<MemorySearchHit>
            {
                Hit("a", 1.0, "postgres staging host is pg-staging-01 on port 5432"),
                Hit("a-copy", 0.95, "postgres staging host is pg-staging-01 on port 5432 today"),
                Hit("b", 0.9, "rotate the staging credentials through the secrets manager")
            };
        }

        private static void DiversifyZeroKeepsOrder()
        {
            List<MemorySearchHit> result = SearchDiversifier.Diversify(DiversityHits(), 0.0, 10);
            TestCase.Require(string.Join(",", result.Select(h => h.Slug)) == "a,a-copy,b", "Diversity 0 should keep the input order.");
            TestCase.Require(SearchDiversifier.Diversify(DiversityHits(), 0.0, 2).Count == 2, "Diversify should cap the count.");
        }

        private static void DiversifyDemotesNearDuplicate()
        {
            List<MemorySearchHit> result = SearchDiversifier.Diversify(DiversityHits(), 0.5, 10);
            TestCase.Require(string.Join(",", result.Select(h => h.Slug)) == "a,b,a-copy", "Diversity should move the near-duplicate below the distinct hit, got " + string.Join(",", result.Select(h => h.Slug)) + ".");
            TestCase.Require(SearchDiversifier.Similarity("Alpha beta", "beta ALPHA") == 1.0 && SearchDiversifier.Similarity("alpha", "gamma") == 0.0, "Similarity should be case-insensitive word overlap.");
        }

        private static void DiversifyValidates()
        {
            TestCase.Throws<ArgumentOutOfRangeException>(() => SearchDiversifier.Diversify(DiversityHits(), 1.5, 3), "Diversity above 1 should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => SearchDiversifier.Diversify(DiversityHits(), 0.5, -1), "A negative count should be rejected.");
            TestCase.Throws<ArgumentNullException>(() => SearchDiversifier.Diversify(null!, 0.5, 3), "Null hits should be rejected.");
        }

        #endregion

        #region Private-Methods-Rerank

        private static async Task RerankTeiAsync()
        {
            using StubResponseHandler handler = new StubResponseHandler("[{\"index\":1,\"score\":0.9},{\"index\":0,\"score\":0.2}]");
            RerankService service = new RerankService(new HttpClient(handler));
            double[] scores = await service.RerankAsync(RerankEndpoint(ApiFormatEnum.Tei), "q", new List<string> { "first", "second", "third" }).ConfigureAwait(false);
            TestCase.Require(scores.Length == 3 && scores[0] == 0.2 && scores[1] == 0.9 && scores[2] == 0.0, "Scores should map back to input order, with omitted passages at 0.");
            TestCase.Require(handler.LastRequestUri != null && handler.LastRequestUri.AbsolutePath == "/rerank", "Tei should post to /rerank, got " + handler.LastRequestUri + ".");
            TestCase.Require((handler.LastRequestBody ?? string.Empty).Contains("\"texts\":[\"first\",\"second\",\"third\"]", StringComparison.Ordinal), "Tei should send the passages as texts.");

            double[] none = await service.RerankAsync(RerankEndpoint(ApiFormatEnum.Tei), "q", new List<string>()).ConfigureAwait(false);
            TestCase.Require(none.Length == 0 && handler.RequestCount == 1, "No passages should mean no request.");
        }

        private static async Task RerankCohereAsync()
        {
            using StubResponseHandler handler = new StubResponseHandler("{\"results\":[{\"index\":0,\"relevance_score\":0.7},{\"index\":1,\"relevance_score\":0.1}]}");
            RerankService service = new RerankService(new HttpClient(handler));
            double[] scores = await service.RerankAsync(RerankEndpoint(ApiFormatEnum.Cohere), "q", new List<string> { "first", "second" }).ConfigureAwait(false);
            TestCase.Require(scores[0] == 0.7 && scores[1] == 0.1, "Cohere scores should come from relevance_score.");
            TestCase.Require(handler.LastRequestUri != null && handler.LastRequestUri.AbsolutePath == "/v1/rerank", "Cohere should post to /v1/rerank.");
            string body = handler.LastRequestBody ?? string.Empty;
            TestCase.Require(body.Contains("\"documents\"", StringComparison.Ordinal) && body.Contains("\"model\":\"ms-marco\"", StringComparison.Ordinal), "Cohere should send documents and the model.");
        }

        private static async Task RerankUnsupportedFormatAsync()
        {
            using StubResponseHandler handler = new StubResponseHandler("[]");
            RerankService service = new RerankService(new HttpClient(handler));
            await TestCase.ThrowsAsync<NotSupportedException>(() => service.RerankAsync(RerankEndpoint(ApiFormatEnum.Ollama), "q", new List<string> { "a" }), "Ollama has no rerank API.").ConfigureAwait(false);
        }

        private static async Task RerankErrorStatusAsync()
        {
            using StubResponseHandler handler = new StubResponseHandler("{\"error\":\"overloaded\"}", HttpStatusCode.ServiceUnavailable);
            RerankService service = new RerankService(new HttpClient(handler));
            await TestCase.ThrowsAsync<InvalidOperationException>(() => service.RerankAsync(RerankEndpoint(ApiFormatEnum.Tei), "q", new List<string> { "a" }), "An error status should throw.").ConfigureAwait(false);
        }

        private static async Task<FilesystemFixture> RerankFixtureAsync(TempSqlite t, StubResponseHandler handler)
        {
            FilesystemFixture f = await FixtureAsync(t, new RerankService(new HttpClient(handler))).ConfigureAwait(false);
            ModelEndpoint endpoint = RerankEndpoint(ApiFormatEnum.Tei);
            endpoint.TenantId = f.Scope.TenantId;
            endpoint = await t.Db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
            f.Scope.RerankEndpointId = endpoint.Id;

            // Keyword search ranks "lexical" (more term repeats) first; the stub reranker prefers the second candidate.
            await PutAsync(f, "lexical", "alpha alpha alpha beta gamma").ConfigureAwait(false);
            await PutAsync(f, "semantic", "alpha beta").ConfigureAwait(false);
            return f;
        }

        private static async Task SearchRerankReordersAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            using StubResponseHandler handler = new StubResponseHandler("[{\"index\":1,\"score\":0.95},{\"index\":0,\"score\":0.2}]");
            FilesystemFixture f = await RerankFixtureAsync(t, handler).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await KeywordAsync(f, "alpha").ConfigureAwait(false);
                TestCase.Require(result.Reranked, "The result should report that it was reranked.");
                TestCase.Require(Slugs(result) == "semantic,lexical", "Hits should follow the rerank scores, got " + Slugs(result) + ".");
                TestCase.Require(result.Hits[0].RerankScore == 0.95 && result.Hits[0].Score == 0.95, "A reranked hit's score should be its rerank score.");
                TestCase.Require((handler.LastRequestBody ?? string.Empty).Contains("alpha alpha alpha", StringComparison.Ordinal), "The reranker should receive the candidate text.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchRerankCutoffAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            using StubResponseHandler handler = new StubResponseHandler("[{\"index\":1,\"score\":0.95},{\"index\":0,\"score\":0.2}]");
            FilesystemFixture f = await RerankFixtureAsync(t, handler).ConfigureAwait(false);
            try
            {
                MemorySearchResult query = await f.Service.SearchAsync(f.Scope, new MemorySearchQuery { QueryText = "alpha", Mode = SearchModeEnum.Keyword, MinRerankScore = 0.5 }).ConfigureAwait(false);
                TestCase.Require(Slugs(query) == "semantic", "The query minRerankScore should drop the weak hit, got " + Slugs(query) + ".");

                f.Scope.RerankMinScore = 0.99;
                MemorySearchResult scope = await KeywordAsync(f, "alpha").ConfigureAwait(false);
                TestCase.Require(scope.Hits.Count == 0 && scope.Reranked, "The scope rerankMinScore should drop every hit below it.");

                MemorySearchResult overridden = await f.Service.SearchAsync(f.Scope, new MemorySearchQuery { QueryText = "alpha", Mode = SearchModeEnum.Keyword, MinRerankScore = 0.0 }).ConfigureAwait(false);
                TestCase.Require(overridden.Hits.Count == 2, "A query minRerankScore should override the scope's.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchRerankOptOutAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            using StubResponseHandler handler = new StubResponseHandler("[{\"index\":1,\"score\":0.95},{\"index\":0,\"score\":0.2}]");
            FilesystemFixture f = await RerankFixtureAsync(t, handler).ConfigureAwait(false);
            try
            {
                MemorySearchResult result = await f.Service.SearchAsync(f.Scope, new MemorySearchQuery { QueryText = "alpha", Mode = SearchModeEnum.Keyword, Rerank = false }).ConfigureAwait(false);
                TestCase.Require(!result.Reranked && Slugs(result) == "lexical,semantic" && handler.RequestCount == 0, "rerank false should skip the reranker.");

                f.Scope.RerankEndpointId = null;
                await TestCase.ThrowsAsync<InvalidOperationException>(
                    () => f.Service.SearchAsync(f.Scope, new MemorySearchQuery { QueryText = "alpha", Mode = SearchModeEnum.Keyword, Rerank = true }),
                    "rerank true without a rerank endpoint should fail.").ConfigureAwait(false);
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static async Task SearchRerankOutageDegradesAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            using StubResponseHandler handler = new StubResponseHandler("{\"error\":\"down\"}", HttpStatusCode.BadGateway);
            FilesystemFixture f = await RerankFixtureAsync(t, handler).ConfigureAwait(false);
            try
            {
                f.Scope.RerankMinScore = 0.99;
                MemorySearchResult result = await KeywordAsync(f, "alpha").ConfigureAwait(false);
                TestCase.Require(!result.Reranked && Slugs(result) == "lexical,semantic", "A failed rerank should keep the retrieval order and skip the rerank cutoff, got " + Slugs(result) + ".");
                TestCase.Require(result.Notice != null && result.Notice.Contains("Reranking failed", StringComparison.Ordinal), "A failed rerank should explain itself in the notice.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        #endregion

        #region Private-Methods-Similarity

        private static async Task UpsertNoSimilarWithoutSemanticAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture f = await FixtureAsync(t).ConfigureAwait(false);
            try
            {
                await PutAsync(f, "one", "The staging database is pg-staging-01.").ConfigureAwait(false);
                Memory two = await PutAsync(f, "two", "The staging database is pg-staging-01.").ConfigureAwait(false);
                TestCase.Require(two.SimilarMemories == null, "A keyword-only store should not report similar memories.");
                TestCase.Require(f.Service.DuplicateCheckEnabled && f.Service.DuplicateSimilarityThreshold == 0.85 && f.Service.DuplicateMaxResults == 3, "Similarity check defaults should be on, 0.85, 3.");
                TestCase.Throws<ArgumentOutOfRangeException>(() => f.Service.DuplicateSimilarityThreshold = 1.5, "A threshold above 1 should be rejected.");
            }
            finally
            {
                DeleteWork(f.Work);
            }
        }

        private static void SimilarMemoriesOmittedWhenNull()
        {
            string none = JsonSerializer.Serialize(new Memory { Slug = "a" });
            TestCase.Require(!none.Contains("SimilarMemories", StringComparison.OrdinalIgnoreCase), "similarMemories should be omitted when null.");
            string some = JsonSerializer.Serialize(new Memory { Slug = "a", SimilarMemories = new List<SimilarMemory> { new SimilarMemory { Slug = "b", Similarity = 0.93 } } });
            TestCase.Require(some.Contains("SimilarMemories", StringComparison.OrdinalIgnoreCase), "similarMemories should be serialized when present.");
        }

        #endregion

        #region Private-Methods-Cache

        private static async Task TtlCacheHitAndExpiryAsync()
        {
            TtlCache<string> cache = new TtlCache<string> { TimeToLive = TimeSpan.FromMilliseconds(150) };
            int loads = 0;
            Func<CancellationToken, Task<string?>> load = _ =>
            {
                loads++;
                return Task.FromResult<string?>("v" + loads);
            };

            string? first = await cache.GetOrLoadAsync("k", load).ConfigureAwait(false);
            string? second = await cache.GetOrLoadAsync("k", load).ConfigureAwait(false);
            TestCase.Require(first == "v1" && second == "v1" && loads == 1, "A second read within the time to live should be served from the cache.");

            await Task.Delay(250).ConfigureAwait(false);
            string? third = await cache.GetOrLoadAsync("k", load).ConfigureAwait(false);
            TestCase.Require(third == "v2" && loads == 2, "A read after the time to live should reload.");

            cache.Remove("k");
            await cache.GetOrLoadAsync("k", load).ConfigureAwait(false);
            TestCase.Require(loads == 3, "A read after Remove should reload.");

            string? missing = await cache.GetOrLoadAsync("absent", _ => Task.FromResult<string?>(null)).ConfigureAwait(false);
            TestCase.Require(missing == null && cache.Count == 1, "A null load should not be cached.");
        }

        private static async Task TtlCacheInvalidationWinsAsync()
        {
            TtlCache<string> cache = new TtlCache<string> { TimeToLive = TimeSpan.FromSeconds(30) };
            TaskCompletionSource<string?> gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string?> racing = cache.GetOrLoadAsync("k", _ => gate.Task);
            cache.Clear();
            gate.SetResult("stale");
            string? raced = await racing.ConfigureAwait(false);
            TestCase.Require(raced == "stale", "The racing reader still gets its value.");

            int loads = 0;
            string? fresh = await cache.GetOrLoadAsync("k", _ =>
            {
                loads++;
                return Task.FromResult<string?>("fresh");
            }).ConfigureAwait(false);
            TestCase.Require(fresh == "fresh" && loads == 1, "A load that raced an invalidation must not be cached.");
        }

        private static async Task LookupCacheInvalidateForWriteAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "before", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
            LookupCache cache = new LookupCache(t.Db, true, TimeSpan.FromSeconds(30));

            Scope? cached = await cache.GetScopeAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            scope.Name = "after";
            await t.Db.Scopes.UpdateAsync(scope).ConfigureAwait(false);

            string path = "/v1.0/api/tenants/" + tenant.Id + "/scopes/" + scope.Id;
            cache.InvalidateForWrite("GET", path);
            cache.InvalidateForWrite("POST", path + "/memories");
            cache.InvalidateForWrite("POST", path + "/memories/search");
            Scope? stillCached = await cache.GetScopeAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(cached != null && stillCached != null && stillCached.Name == "before", "Reads and memory writes should not invalidate the cached scope.");

            cache.InvalidateForWrite("PUT", path);
            Scope? reloaded = await cache.GetScopeAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(reloaded != null && reloaded.Name == "after", "A scope write should invalidate the cached scope.");
        }

        private static async Task LookupCacheDisabledAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            Scope scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "before", StoreProvider = StoreProviderEnum.Filesystem }).ConfigureAwait(false);
            LookupCache cache = new LookupCache(t.Db, false, TimeSpan.FromSeconds(30));

            await cache.GetScopeAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            scope.Name = "after";
            await t.Db.Scopes.UpdateAsync(scope).ConfigureAwait(false);
            Scope? read = await cache.GetScopeAsync(tenant.Id, scope.Id).ConfigureAwait(false);
            TestCase.Require(read != null && read.Name == "after" && !cache.Enabled, "A disabled cache should read through.");
        }

        #endregion
    }
}
