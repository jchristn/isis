namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Stores;
    using Isis.Core.Stores.Filesystem;
    using Isis.Core.Stores.RecallDb;
    using Isis.Core.Stores.Verbex;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone test suite exercising the Isis memory stores: the filesystem store (hierarchy and
    /// single-file layouts), the store factory, provider capabilities, and the unconfigured RecallDB and
    /// Verbex stores. These tests use only the real store APIs and temporary directories; they touch no
    /// external services.
    /// </summary>
    public static class StoreSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the store test suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "store",
                "Isis Store Suite",
                new List<TestCaseDescriptor>
                {
                    // Filesystem: hierarchy layout.
                    TestCase.Async("store", "fs-hier-ensure-creates-dir", "Filesystem hierarchy EnsureScope creates the target directory", FsHierEnsureCreatesDirAsync),
                    TestCase.Async("store", "fs-hier-upsert-returns-file-key", "Filesystem hierarchy Upsert returns an existing file path", FsHierUpsertReturnsFileKeyAsync),
                    TestCase.Async("store", "fs-hier-reupsert-single-file", "Filesystem hierarchy re-upsert of a slug keeps a single file", FsHierReupsertSingleFileAsync),
                    TestCase.Async("store", "fs-hier-delete-removes-file", "Filesystem hierarchy Delete removes the file", FsHierDeleteRemovesFileAsync),
                    TestCase.Async("store", "fs-hier-search-keyword-hit", "Filesystem hierarchy keyword search returns a scored hit with a snippet", FsHierSearchKeywordHitAsync),
                    TestCase.Async("store", "fs-hier-search-effective-keyword", "Filesystem hierarchy search reports keyword as the effective mode", FsHierSearchEffectiveKeywordAsync),
                    TestCase.Async("store", "fs-hier-hybrid-notice", "Filesystem hierarchy hybrid request produces a degradation notice", FsHierHybridNoticeAsync),
                    TestCase.Async("store", "fs-hier-category-filter-nomatch", "Filesystem hierarchy category filter excludes non-matching categories", FsHierCategoryFilterNoMatchAsync),
                    TestCase.Async("store", "fs-hier-query-nomatch-zero", "Filesystem hierarchy query with no overlap returns zero hits", FsHierQueryNoMatchZeroAsync),
                    TestCase.Async("store", "fs-hier-tokenbudget-truncates", "Filesystem hierarchy small token budget truncates the snippet", FsHierTokenBudgetTruncatesAsync),
                    TestCase.Async("store", "fs-hier-score-ordering", "Filesystem hierarchy orders hits by descending score", FsHierScoreOrderingAsync),

                    // Filesystem: single-file layout.
                    TestCase.Async("store", "fs-single-one-file", "Filesystem single-file writes exactly one isis-memory.md", FsSingleOneFileAsync),
                    TestCase.Async("store", "fs-single-both-searchable", "Filesystem single-file makes both memories searchable", FsSingleBothSearchableAsync),
                    TestCase.Async("store", "fs-single-update-no-dup", "Filesystem single-file re-upsert does not duplicate a memory", FsSingleUpdateNoDupAsync),
                    TestCase.Async("store", "fs-single-delete-one-keeps-other", "Filesystem single-file delete removes only the targeted block", FsSingleDeleteOneKeepsOtherAsync),

                    // Filesystem: missing target path.
                    TestCase.Async("store", "fs-null-target-ensure-throws", "Filesystem EnsureScope without a target path throws", FsNullTargetEnsureThrowsAsync),
                    TestCase.Async("store", "fs-null-target-upsert-throws", "Filesystem Upsert without a target path throws", FsNullTargetUpsertThrowsAsync),

                    // Factory.
                    TestCase.Async("store", "factory-filesystem-type", "Factory creates a FilesystemMemoryStore for Filesystem", FactoryFilesystemTypeAsync),
                    TestCase.Async("store", "factory-recalldb-type", "Factory creates a RecallDbMemoryStore for RecallDb", FactoryRecallDbTypeAsync),
                    TestCase.Async("store", "factory-verbex-type", "Factory creates a VerbexMemoryStore for Verbex", FactoryVerbexTypeAsync),
                    TestCase.Async("store", "factory-scope-uses-provider", "Factory create-from-scope honors the scope provider", FactoryScopeUsesProviderAsync),
                    TestCase.Async("store", "factory-scope-options-null-endpoint", "Factory returns an unconfigured RecallDB store when the endpoint is null", FactoryScopeOptionsNullEndpointAsync),

                    // Capabilities.
                    TestCase.Async("store", "caps-recalldb", "RecallDB advertises semantic, hybrid, keyword, and embeddings", CapsRecallDbAsync),
                    TestCase.Async("store", "caps-verbex", "Verbex advertises keyword only, no semantic/hybrid/embeddings", CapsVerbexAsync),
                    TestCase.Async("store", "caps-filesystem", "Filesystem advertises keyword only, no semantic/embeddings", CapsFilesystemAsync),

                    // Unconfigured RecallDB.
                    TestCase.Async("store", "recalldb-unconfigured-ensure-throws", "Unconfigured RecallDB EnsureScope throws NotSupported", RecallDbUnconfiguredEnsureThrowsAsync),
                    TestCase.Async("store", "recalldb-unconfigured-upsert-throws", "Unconfigured RecallDB Upsert throws NotSupported", RecallDbUnconfiguredUpsertThrowsAsync),
                    TestCase.Async("store", "recalldb-unconfigured-search-throws", "Unconfigured RecallDB Search throws NotSupported", RecallDbUnconfiguredSearchThrowsAsync),
                    TestCase.Async("store", "recalldb-unconfigured-delete-throws", "Unconfigured RecallDB Delete throws NotSupported", RecallDbUnconfiguredDeleteThrowsAsync),

                    // Verbex (not wired).
                    TestCase.Async("store", "verbex-ensure-throws", "Verbex EnsureScope throws NotSupported", VerbexEnsureThrowsAsync),
                    TestCase.Async("store", "verbex-upsert-throws", "Verbex Upsert throws NotSupported", VerbexUpsertThrowsAsync),
                    TestCase.Async("store", "verbex-search-throws", "Verbex Search throws NotSupported", VerbexSearchThrowsAsync),
                    TestCase.Async("store", "verbex-delete-throws", "Verbex Delete throws NotSupported", VerbexDeleteThrowsAsync)
                });
        }

        #endregion

        #region Private-Methods-Filesystem-Hierarchy

        private static async Task FsHierEnsureCreatesDirAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                TestCase.Require(!Directory.Exists(work), "The target directory should not exist before EnsureScope.");
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                TestCase.Require(Directory.Exists(work), "EnsureScope should create the target directory.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierUpsertReturnsFileKeyAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                Memory memory = Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar.");
                string key = await store.UpsertAsync(scope, memory, null).ConfigureAwait(false);

                TestCase.Require(!string.IsNullOrEmpty(key), "Upsert should return a non-empty store key.");
                TestCase.Require(File.Exists(key), "The store key should point at an existing file.");
                TestCase.Require(key.EndsWith(".md", StringComparison.OrdinalIgnoreCase), "The store key should be a markdown file path.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierReupsertSingleFileAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the collar."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                string categoryDir = Path.Combine(work, "cat_1");
                string[] files = Directory.GetFiles(categoryDir, "*.md", SearchOption.AllDirectories);
                TestCase.Require(files.Length == 1, "Re-upserting the same slug must keep a single file, found " + files.Length + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierDeleteRemovesFileAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                Memory memory = Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar.");
                string key = await store.UpsertAsync(scope, memory, null).ConfigureAwait(false);
                TestCase.Require(File.Exists(key), "The memory file should exist after upsert.");

                await store.DeleteAsync(scope, memory).ConfigureAwait(false);
                TestCase.Require(!File.Exists(key), "Delete should remove the memory file.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierSearchKeywordHitAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "sleeve collar", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count >= 1, "Expected at least one hit for an overlapping keyword.");
                MemorySearchHit hit = result.Hits[0];
                TestCase.Require(hit.Slug == "grip", "Expected the hit slug to match 'grip', got '" + hit.Slug + "'.");
                TestCase.Require(!string.IsNullOrEmpty(hit.Snippet), "Expected a non-empty snippet.");
                TestCase.Require(hit.Score > 0.0, "Expected a positive score, got " + hit.Score + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierSearchEffectiveKeywordAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.EffectiveMode == SearchModeEnum.Keyword, "Filesystem search should report keyword as the effective mode.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierHybridNoticeAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Hybrid, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.EffectiveMode == SearchModeEnum.Keyword, "A hybrid request should still be served as keyword.");
                TestCase.Require(!string.IsNullOrEmpty(result.Notice), "A hybrid request against the filesystem store should carry a degradation notice.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierCategoryFilterNoMatchAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword, CategoryFilter = "cat_nomatch", TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 0, "A non-matching category filter should return zero hits, got " + result.Hits.Count + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierQueryNoMatchZeroAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "zzznotpresentanywhere", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 0, "A query that matches nothing should return zero hits, got " + result.Hits.Count + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierTokenBudgetTruncatesAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                string body = "Win the grip; control the sleeve and collar to dominate the exchange completely.";
                await store.UpsertAsync(scope, Mem(scope, "grip", "Grip", body), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword, TokenBudget = 20, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count >= 1, "Expected a hit for the token-budget case.");
                string snippet = result.Hits[0].Snippet;
                TestCase.Require(!string.IsNullOrEmpty(snippet), "Expected a non-empty snippet.");
                // window is 20 chars; ellipses may add at most two characters on each side.
                TestCase.Require(snippet.Length <= 24, "A 20-char token budget should bound the snippet length, got " + snippet.Length + ".");
                TestCase.Require(snippet.Length < body.Length, "A budgeted snippet should be shorter than the full body.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsHierScoreOrderingAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = HierScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                // 'double' mentions grip twice; 'single' mentions it once.
                await store.UpsertAsync(scope, Mem(scope, "double", "Double", "Grip and grip again to win the exchange."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "single", "Single", "Grip the collar tightly."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 2, "Expected two hits, got " + result.Hits.Count + ".");
                TestCase.Require(result.Hits[0].Slug == "double", "Expected the higher-frequency memory ranked first, got '" + result.Hits[0].Slug + "'.");
                TestCase.Require(result.Hits[0].Score >= result.Hits[1].Score, "Hits should be ordered by descending score.");
                TestCase.Require(result.Hits[0].Score > result.Hits[1].Score, "The double-mention memory should outscore the single-mention memory.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        #endregion

        #region Private-Methods-Filesystem-SingleFile

        private static async Task FsSingleOneFileAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = SingleFileScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                await store.UpsertAsync(scope, Mem(scope, "grip-a", "Grip A", "Win the grip; control the collar."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip-b", "Grip B", "Grip the collar tightly to win."), null).ConfigureAwait(false);

                string[] files = Directory.GetFiles(work);
                TestCase.Require(files.Length == 1, "Single-file layout should produce exactly one file, found " + files.Length + ".");
                TestCase.Require(Path.GetFileName(files[0]) == "isis-memory.md", "The single file should be named isis-memory.md, got '" + Path.GetFileName(files[0]) + "'.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsSingleBothSearchableAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = SingleFileScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                await store.UpsertAsync(scope, Mem(scope, "grip-a", "Grip A", "Win the grip; control the collar."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip-b", "Grip B", "Grip the collar tightly to win."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "collar", Mode = SearchModeEnum.Keyword, TopK = 10 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 2, "Both single-file memories should be searchable, got " + result.Hits.Count + " hits.");
                List<string?> slugs = result.Hits.Select(h => h.Slug).ToList();
                TestCase.Require(slugs.Contains("grip-a") && slugs.Contains("grip-b"), "Expected both slugs among the hits.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsSingleUpdateNoDupAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = SingleFileScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                await store.UpsertAsync(scope, Mem(scope, "grip-a", "Grip A", "Win the grip; control the collar."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "grip-b", "Grip B", "Grip the collar tightly to win."), null).ConfigureAwait(false);

                // Re-upsert grip-a with new content; it must replace, not duplicate.
                await store.UpsertAsync(scope, Mem(scope, "grip-a", "Grip A", "Fight for the collar and win the grip."), null).ConfigureAwait(false);

                string[] files = Directory.GetFiles(work);
                TestCase.Require(files.Length == 1, "Re-upsert should not create additional files, found " + files.Length + ".");

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "collar", Mode = SearchModeEnum.Keyword, TopK = 10 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 2, "Re-upsert must not duplicate a memory, got " + result.Hits.Count + " hits.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task FsSingleDeleteOneKeepsOtherAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = SingleFileScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                Memory a = Mem(scope, "grip-a", "Grip A", "Win the grip; control the collar.");
                Memory b = Mem(scope, "grip-b", "Grip B", "Grip the collar tightly to win.");
                await store.UpsertAsync(scope, a, null).ConfigureAwait(false);
                await store.UpsertAsync(scope, b, null).ConfigureAwait(false);

                await store.DeleteAsync(scope, a).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "collar", Mode = SearchModeEnum.Keyword, TopK = 10 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 1, "Deleting one block should leave exactly one searchable memory, got " + result.Hits.Count + ".");
                TestCase.Require(result.Hits[0].Slug == "grip-b", "The remaining memory should be 'grip-b', got '" + result.Hits[0].Slug + "'.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        #endregion

        #region Private-Methods-Filesystem-NoTarget

        private static async Task FsNullTargetEnsureThrowsAsync()
        {
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.Hierarchy, TargetPath = null };
            IMemoryStore store = MemoryStoreFactory.Create(scope);
            await TestCase.ThrowsAsync<InvalidOperationException>(
                () => store.EnsureScopeAsync(scope),
                "EnsureScope on a filesystem scope with no target path should throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static async Task FsNullTargetUpsertThrowsAsync()
        {
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.Hierarchy, TargetPath = null };
            IMemoryStore store = MemoryStoreFactory.Create(scope);
            Memory memory = Mem(scope, "grip", "Grip", "Win the grip; control the sleeve and collar.");
            await TestCase.ThrowsAsync<InvalidOperationException>(
                () => store.UpsertAsync(scope, memory, null),
                "Upsert on a filesystem scope with no target path should throw InvalidOperationException.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Factory

        private static Task FactoryFilesystemTypeAsync()
        {
            IMemoryStore store = MemoryStoreFactory.Create(StoreProviderEnum.Filesystem);
            TestCase.Require(store is FilesystemMemoryStore, "Filesystem provider should produce a FilesystemMemoryStore.");
            return Task.CompletedTask;
        }

        private static Task FactoryRecallDbTypeAsync()
        {
            IMemoryStore store = MemoryStoreFactory.Create(StoreProviderEnum.RecallDb);
            TestCase.Require(store is RecallDbMemoryStore, "RecallDb provider should produce a RecallDbMemoryStore.");
            return Task.CompletedTask;
        }

        private static Task FactoryVerbexTypeAsync()
        {
            IMemoryStore store = MemoryStoreFactory.Create(StoreProviderEnum.Verbex);
            TestCase.Require(store is VerbexMemoryStore, "Verbex provider should produce a VerbexMemoryStore.");
            return Task.CompletedTask;
        }

        private static Task FactoryScopeUsesProviderAsync()
        {
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem };
            IMemoryStore store = MemoryStoreFactory.Create(scope);
            TestCase.Require(store is FilesystemMemoryStore, "Create(scope) should honor the scope's store provider.");
            return Task.CompletedTask;
        }

        private static async Task FactoryScopeOptionsNullEndpointAsync()
        {
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.RecallDb };
            IMemoryStore store = MemoryStoreFactory.Create(scope, new StoreOptions { RecallDbEndpoint = null });
            TestCase.Require(store is RecallDbMemoryStore, "A RecallDb scope with no endpoint should still produce a RecallDbMemoryStore.");
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.EnsureScopeAsync(scope),
                "The store should be unconfigured and throw NotSupportedException on use.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Capabilities

        private static Task CapsRecallDbAsync()
        {
            StoreCapabilities caps = new RecallDbMemoryStore().Capabilities;
            TestCase.Require(caps.SupportsSemantic, "RecallDB should support semantic search.");
            TestCase.Require(caps.SupportsHybrid, "RecallDB should support hybrid search.");
            TestCase.Require(caps.RequiresEmbedding, "RecallDB should require embeddings.");
            TestCase.Require(caps.SupportsKeyword, "RecallDB should support keyword search.");
            return Task.CompletedTask;
        }

        private static Task CapsVerbexAsync()
        {
            StoreCapabilities caps = new VerbexMemoryStore().Capabilities;
            TestCase.Require(!caps.SupportsSemantic, "Verbex should not support semantic search.");
            TestCase.Require(!caps.SupportsHybrid, "Verbex should not support hybrid search.");
            TestCase.Require(caps.SupportsKeyword, "Verbex should support keyword search.");
            TestCase.Require(!caps.RequiresEmbedding, "Verbex should not require embeddings.");
            return Task.CompletedTask;
        }

        private static Task CapsFilesystemAsync()
        {
            StoreCapabilities caps = new FilesystemMemoryStore().Capabilities;
            TestCase.Require(!caps.SupportsSemantic, "Filesystem should not support semantic search.");
            TestCase.Require(!caps.RequiresEmbedding, "Filesystem should not require embeddings.");
            TestCase.Require(caps.SupportsKeyword, "Filesystem should support keyword search.");
            return Task.CompletedTask;
        }

        #endregion

        #region Private-Methods-RecallDb-Unconfigured

        private static async Task RecallDbUnconfiguredEnsureThrowsAsync()
        {
            IMemoryStore store = new RecallDbMemoryStore();
            Scope scope = RecallScope();
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.EnsureScopeAsync(scope),
                "An unconfigured RecallDB store should throw NotSupportedException from EnsureScope.").ConfigureAwait(false);
        }

        private static async Task RecallDbUnconfiguredUpsertThrowsAsync()
        {
            IMemoryStore store = new RecallDbMemoryStore();
            Scope scope = RecallScope();
            Memory memory = new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = "cat_1", Slug = "grip", Title = "Grip", Body = "Win the grip." };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.UpsertAsync(scope, memory, new float[] { 0.1f, 0.2f }),
                "An unconfigured RecallDB store should throw NotSupportedException from Upsert.").ConfigureAwait(false);
        }

        private static async Task RecallDbUnconfiguredSearchThrowsAsync()
        {
            IMemoryStore store = new RecallDbMemoryStore();
            Scope scope = RecallScope();
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword }, null),
                "An unconfigured RecallDB store should throw NotSupportedException from Search.").ConfigureAwait(false);
        }

        private static async Task RecallDbUnconfiguredDeleteThrowsAsync()
        {
            IMemoryStore store = new RecallDbMemoryStore();
            Scope scope = RecallScope();
            Memory memory = new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = "cat_1", Slug = "grip", Title = "Grip", Body = "Win the grip." };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.DeleteAsync(scope, memory),
                "An unconfigured RecallDB store should throw NotSupportedException from Delete.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Verbex

        private static async Task VerbexEnsureThrowsAsync()
        {
            IMemoryStore store = new VerbexMemoryStore();
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Verbex };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.EnsureScopeAsync(scope),
                "The Verbex store should throw NotSupportedException from EnsureScope.").ConfigureAwait(false);
        }

        private static async Task VerbexUpsertThrowsAsync()
        {
            IMemoryStore store = new VerbexMemoryStore();
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Verbex };
            Memory memory = new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = "cat_1", Slug = "grip", Title = "Grip", Body = "Win the grip." };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.UpsertAsync(scope, memory, null),
                "The Verbex store should throw NotSupportedException from Upsert.").ConfigureAwait(false);
        }

        private static async Task VerbexSearchThrowsAsync()
        {
            IMemoryStore store = new VerbexMemoryStore();
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Verbex };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.SearchAsync(scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword }, null),
                "The Verbex store should throw NotSupportedException from Search.").ConfigureAwait(false);
        }

        private static async Task VerbexDeleteThrowsAsync()
        {
            IMemoryStore store = new VerbexMemoryStore();
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Verbex };
            Memory memory = new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = "cat_1", Slug = "grip", Title = "Grip", Body = "Win the grip." };
            await TestCase.ThrowsAsync<NotSupportedException>(
                () => store.DeleteAsync(scope, memory),
                "The Verbex store should throw NotSupportedException from Delete.").ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods-Helpers

        private static string WorkDir()
        {
            return Path.Combine(Path.GetTempPath(), "isis-store-" + Guid.NewGuid().ToString("N"));
        }

        private static Scope HierScope(string dir)
        {
            return new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.Hierarchy, TargetPath = dir };
        }

        private static Scope SingleFileScope(string dir)
        {
            return new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.SingleFile, TargetPath = dir };
        }

        private static Scope RecallScope()
        {
            return new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.RecallDb };
        }

        private static Memory Mem(Scope scope, string slug, string title, string body, string categoryId = "cat_1")
        {
            return new Memory { TenantId = "ten_x", ScopeId = scope.Id, CategoryId = categoryId, Slug = slug, Title = title, Body = body };
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        #endregion
    }
}
