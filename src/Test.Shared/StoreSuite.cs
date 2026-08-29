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

                    // Filesystem: OKF bundle layout (positive).
                    TestCase.Async("store", "okf-upsert-writes-frontmatter", "OKF upsert writes a category/slug.md file with YAML frontmatter", OkfUpsertWritesFrontmatterAsync),
                    TestCase.Async("store", "okf-upsert-generates-index", "OKF upsert generates a root index.md linking the memory", OkfUpsertGeneratesIndexAsync),
                    TestCase.Async("store", "okf-roundtrip-fidelity", "OKF Serialize then Parse preserves every memory field", OkfRoundtripFidelityAsync),
                    TestCase.Async("store", "okf-search-keyword-hit", "OKF keyword search returns a scored hit via the frontmatter read path", OkfSearchKeywordHitAsync),
                    TestCase.Async("store", "okf-index-not-a-memory", "OKF search does not return the generated index.md as a memory", OkfIndexNotAMemoryAsync),
                    TestCase.Async("store", "okf-reupsert-single-file", "OKF re-upsert of a slug keeps a single file", OkfReupsertSingleFileAsync),
                    TestCase.Async("store", "okf-delete-removes-and-reindexes", "OKF delete removes the file and drops it from the index", OkfDeleteRemovesAndReindexesAsync),
                    TestCase.Async("store", "okf-foreign-bundle-import", "OKF reads a foreign bundle (bare scalars, unknown type, flow tags)", OkfForeignBundleImportAsync),

                    // Filesystem: OKF bundle layout (negative / tolerance).
                    TestCase.Async("store", "okf-null-target-upsert-throws", "OKF Upsert without a target path throws", OkfNullTargetUpsertThrowsAsync),
                    TestCase.Async("store", "okf-parse-no-frontmatter", "OKF Parse of a file with no frontmatter falls back without throwing", OkfParseNoFrontmatterAsync),
                    TestCase.Async("store", "okf-parse-malformed-metadata", "OKF Parse ignores a malformed metadata block", OkfParseMalformedMetadataAsync),
                    TestCase.Async("store", "okf-parse-unterminated-frontmatter", "OKF Parse tolerates an unterminated frontmatter block", OkfParseUnterminatedFrontmatterAsync),
                    TestCase.Async("store", "okf-parse-empty-content", "OKF Parse of empty content yields fallbacks and does not throw", OkfParseEmptyContentAsync),

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

        #region Private-Methods-Filesystem-Okf

        private static async Task OkfUpsertWritesFrontmatterAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                string key = await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "One row per completed order."), null).ConfigureAwait(false);
                TestCase.Require(File.Exists(key), "OKF upsert should write the memory file.");
                TestCase.Require(key.Replace('\\', '/').EndsWith("cat_1/orders.md", StringComparison.OrdinalIgnoreCase), "The file should live under <category>/<slug>.md, got '" + key + "'.");

                string text = await File.ReadAllTextAsync(key).ConfigureAwait(false);
                TestCase.Require(text.StartsWith("---", StringComparison.Ordinal), "An OKF document should open with a YAML frontmatter delimiter.");
                TestCase.Require(text.Contains("type: \"Project\""), "Frontmatter should carry the type field.");
                TestCase.Require(text.Contains("title: \"Orders\""), "Frontmatter should carry the title field.");
                TestCase.Require(text.Contains("slug: \"orders\""), "Frontmatter should carry the slug field.");
                TestCase.Require(text.Contains("category: \"cat_1\""), "Frontmatter should carry the category field.");
                TestCase.Require(text.Contains("One row per completed order."), "The body should be preserved.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfUpsertGeneratesIndexAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "One row per completed order."), null).ConfigureAwait(false);

                string indexPath = Path.Combine(work, "index.md");
                TestCase.Require(File.Exists(indexPath), "OKF upsert should generate a root index.md.");
                string index = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
                TestCase.Require(index.Contains("type: Index"), "The index should declare the reserved Index type.");
                TestCase.Require(index.Contains("](cat_1/orders.md)"), "The index should link the memory by its relative path.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static Task OkfRoundtripFidelityAsync()
        {
            Memory original = new Memory
            {
                TenantId = "ten_x",
                ScopeId = "scp_x",
                CategoryId = "tables",
                Slug = "orders",
                Title = "Orders",
                Type = MemoryTypeEnum.Feedback,
                Summary = "One row per completed order.",
                Resource = "https://console.example.com/t/orders",
                Body = "# Schema\n| col | type |\n|-----|------|\n| id | STRING |",
                Tags = new List<string> { "sales", "revenue" },
                Links = new List<string> { "customers", "line-items" },
                Metadata = new Dictionary<string, string> { { "confidence", "high" }, { "owner", "data-team" } },
                Author = "agent-42",
                SessionId = "sess_9",
                Model = "gpt-oss:20b",
                Version = 3,
                Salience = 0.42,
                CreatedUtc = new DateTime(2026, 5, 28, 14, 30, 0, DateTimeKind.Utc),
                LastUpdateUtc = new DateTime(2026, 6, 1, 9, 15, 30, DateTimeKind.Utc)
            };

            string doc = OkfDocument.Serialize(original);
            Memory parsed = OkfDocument.Parse(doc, "fallback-slug", "fallback-cat");

            TestCase.Require(parsed.Slug == original.Slug, "Slug should round-trip.");
            TestCase.Require(parsed.CategoryId == original.CategoryId, "Category should round-trip.");
            TestCase.Require(parsed.Title == original.Title, "Title should round-trip.");
            TestCase.Require(parsed.Type == original.Type, "Type should round-trip, got '" + parsed.Type + "'.");
            TestCase.Require(parsed.Summary == original.Summary, "Summary/description should round-trip.");
            TestCase.Require(parsed.Resource == original.Resource, "Resource should round-trip.");
            TestCase.Require(parsed.Body.TrimEnd('\n') == original.Body.TrimEnd('\n'), "Body should round-trip.");
            TestCase.Require(string.Join(",", parsed.Tags) == "sales,revenue", "Tags should round-trip, got '" + string.Join(",", parsed.Tags) + "'.");
            TestCase.Require(string.Join(",", parsed.Links) == "customers,line-items", "Links should round-trip, got '" + string.Join(",", parsed.Links) + "'.");
            TestCase.Require(parsed.Author == original.Author, "Author should round-trip.");
            TestCase.Require(parsed.SessionId == original.SessionId, "SessionId should round-trip.");
            TestCase.Require(parsed.Model == original.Model, "Model should round-trip.");
            TestCase.Require(parsed.Version == original.Version, "Version should round-trip, got " + parsed.Version + ".");
            TestCase.Require(Math.Abs(parsed.Salience - original.Salience) < 0.0001, "Salience should round-trip, got " + parsed.Salience + ".");
            TestCase.Require(parsed.Metadata.Count == 2 && parsed.Metadata["confidence"] == "high" && parsed.Metadata["owner"] == "data-team", "Metadata should round-trip.");
            TestCase.Require(parsed.CreatedUtc == original.CreatedUtc, "CreatedUtc should round-trip, got " + parsed.CreatedUtc.ToString("O") + ".");
            TestCase.Require(parsed.LastUpdateUtc == original.LastUpdateUtc, "LastUpdateUtc should round-trip, got " + parsed.LastUpdateUtc.ToString("O") + ".");
            return Task.CompletedTask;
        }

        private static async Task OkfSearchKeywordHitAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "One row per completed customer order."), null).ConfigureAwait(false);

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "completed order", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count >= 1, "Expected at least one hit reading OKF frontmatter files.");
                TestCase.Require(result.Hits[0].Slug == "orders", "Expected the hit slug to be 'orders', got '" + result.Hits[0].Slug + "'.");
                TestCase.Require(!string.IsNullOrEmpty(result.Hits[0].Snippet), "Expected a non-empty snippet.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfIndexNotAMemoryAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);
                // "Orders" appears both in the memory (title) AND in the generated index.md (as a link label),
                // so a single hit proves the reserved index.md is excluded from search.
                await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "One row per completed order."), null).ConfigureAwait(false);
                string indexText = await File.ReadAllTextAsync(Path.Combine(work, "index.md")).ConfigureAwait(false);
                TestCase.Require(indexText.Contains("Orders"), "The index.md should mention 'Orders' for this test to be meaningful.");

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "Orders", Mode = SearchModeEnum.Keyword, TopK = 10 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.All(h => h.Slug != "index"), "The reserved index.md must never be returned as a memory.");
                TestCase.Require(result.Hits.Count == 1, "Only the real memory should match, not the index.md, got " + result.Hits.Count + " hits.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfReupsertSingleFileAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "Original body."), null).ConfigureAwait(false);
                await store.UpsertAsync(scope, Mem(scope, "orders", "Orders", "Revised body with more detail."), null).ConfigureAwait(false);

                string categoryDir = Path.Combine(work, "cat_1");
                string[] files = Directory.GetFiles(categoryDir, "*.md", SearchOption.AllDirectories);
                TestCase.Require(files.Length == 1, "Re-upserting the same slug must keep a single file, found " + files.Length + ".");

                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "revised detail", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Count == 1, "The re-upserted memory should be the single searchable copy, got " + result.Hits.Count + ".");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfDeleteRemovesAndReindexesAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                Memory keep = Mem(scope, "orders", "Orders", "One row per order.");
                Memory drop = Mem(scope, "customers", "Customers", "One row per customer.");
                await store.UpsertAsync(scope, keep, null).ConfigureAwait(false);
                string dropKey = await store.UpsertAsync(scope, drop, null).ConfigureAwait(false);

                await store.DeleteAsync(scope, drop).ConfigureAwait(false);
                TestCase.Require(!File.Exists(dropKey), "Delete should remove the memory file.");

                string index = await File.ReadAllTextAsync(Path.Combine(work, "index.md")).ConfigureAwait(false);
                TestCase.Require(!index.Contains("customers.md"), "The deleted memory should be dropped from the index.");
                TestCase.Require(index.Contains("orders.md"), "The surviving memory should remain in the index.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfForeignBundleImportAsync()
        {
            string work = WorkDir();
            try
            {
                Scope scope = OkfScope(work);
                IMemoryStore store = MemoryStoreFactory.Create(scope);
                await store.EnsureScopeAsync(scope).ConfigureAwait(false);

                // A foreign OKF document: bare (unquoted) scalars, a flow-style tag list, an unknown type,
                // and no Isis provenance extras — the shape a non-Isis producer (e.g. an enrichment agent) emits.
                string foreign =
                    "---\n" +
                    "type: BigQuery Table\n" +
                    "title: Orders\n" +
                    "description: One row per completed customer order.\n" +
                    "resource: https://console.cloud.google.com/bigquery\n" +
                    "tags: [sales, revenue]\n" +
                    "timestamp: 2026-05-28T14:30:00Z\n" +
                    "---\n" +
                    "# Schema\n| order_id | STRING |\n";
                string dir = Path.Combine(work, "datasets");
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(Path.Combine(dir, "orders.md"), foreign).ConfigureAwait(false);

                // Parsed directly: unknown type maps to Reference with the original preserved; fields survive.
                Memory parsed = OkfDocument.Parse(foreign, "orders", "datasets");
                TestCase.Require(parsed.Type == MemoryTypeEnum.Reference, "An unknown foreign type should map to Reference, got '" + parsed.Type + "'.");
                TestCase.Require(parsed.Metadata.TryGetValue("sourceType", out string? src) && src == "BigQuery Table", "The original foreign type should be preserved in metadata.");
                TestCase.Require(parsed.Title == "Orders", "A bare scalar title should parse.");
                TestCase.Require(parsed.Resource == "https://console.cloud.google.com/bigquery", "A bare URI resource should parse.");
                TestCase.Require(string.Join(",", parsed.Tags) == "sales,revenue", "A flow-style tag list should parse, got '" + string.Join(",", parsed.Tags) + "'.");
                TestCase.Require(parsed.CategoryId == "datasets", "The category should fall back to the parent directory.");

                // And the store picks it up through its own read path.
                MemorySearchResult result = await store.SearchAsync(scope, new MemorySearchQuery { QueryText = "customer order", Mode = SearchModeEnum.Keyword, TopK = 5 }, null).ConfigureAwait(false);
                TestCase.Require(result.Hits.Any(h => h.Slug == "orders"), "The foreign bundle document should be searchable.");
            }
            finally
            {
                TryDeleteDir(work);
            }
        }

        private static async Task OkfNullTargetUpsertThrowsAsync()
        {
            Scope scope = new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.OkfBundle, TargetPath = null };
            IMemoryStore store = MemoryStoreFactory.Create(scope);
            Memory memory = Mem(scope, "orders", "Orders", "One row per order.");
            await TestCase.ThrowsAsync<InvalidOperationException>(
                () => store.UpsertAsync(scope, memory, null),
                "OKF upsert on a filesystem scope with no target path should throw InvalidOperationException.").ConfigureAwait(false);
        }

        private static Task OkfParseNoFrontmatterAsync()
        {
            Memory parsed = OkfDocument.Parse("Just a plain markdown body, no frontmatter.", "my-slug", "my-cat");
            TestCase.Require(parsed.Slug == "my-slug", "Slug should fall back to the filename when frontmatter is absent.");
            TestCase.Require(parsed.CategoryId == "my-cat", "Category should fall back to the directory when frontmatter is absent.");
            TestCase.Require(parsed.Body.Contains("plain markdown body"), "The whole content should be treated as the body.");
            return Task.CompletedTask;
        }

        private static Task OkfParseMalformedMetadataAsync()
        {
            string doc = "---\ntype: \"Project\"\nslug: \"x\"\ncategory: \"c\"\nmetadata: {not valid json]\n---\nBody.";
            Memory parsed = OkfDocument.Parse(doc, "x", "c");
            TestCase.Require(parsed.Metadata.Count == 0, "A malformed metadata block should be ignored, not throw, leaving metadata empty.");
            TestCase.Require(parsed.Body.TrimEnd('\n') == "Body.", "The body should still parse around a malformed metadata block.");
            return Task.CompletedTask;
        }

        private static Task OkfParseUnterminatedFrontmatterAsync()
        {
            string doc = "---\ntype: \"Project\"\ntitle: \"Orphan\"\nno closing delimiter and then body text";
            Memory parsed = OkfDocument.Parse(doc, "x", "c");
            // Tolerant: no exception; content after the opener becomes the body.
            TestCase.Require(parsed.Body.Contains("body text"), "An unterminated frontmatter should be treated tolerantly as body.");
            return Task.CompletedTask;
        }

        private static Task OkfParseEmptyContentAsync()
        {
            Memory parsed = OkfDocument.Parse(string.Empty, "fallback", "cat");
            TestCase.Require(parsed.Slug == "fallback", "Empty content should still yield the fallback slug.");
            TestCase.Require(parsed.CategoryId == "cat", "Empty content should still yield the fallback category.");
            TestCase.Require(parsed.Body == string.Empty, "Empty content should yield an empty body.");
            return Task.CompletedTask;
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

        private static Scope OkfScope(string dir)
        {
            return new Scope { TenantId = "ten_x", Name = "Memory Index", StoreProvider = StoreProviderEnum.Filesystem, FilesystemLayout = FilesystemLayoutEnum.OkfBundle, TargetPath = dir };
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
