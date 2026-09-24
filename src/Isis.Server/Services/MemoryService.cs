namespace Isis.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Observability;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.Core.Stores.RecallDb;

    /// <summary>
    /// Coordinates the memory index (relational metadata) with the scope's memory store (content and
    /// retrieval). Writes are idempotent on (scope, category, slug). For stores that require embeddings
    /// (RecallDB), the scope's configured embedding endpoint is used to vectorize content and queries.
    /// </summary>
    public class MemoryService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly EmbeddingService? _EmbeddingService;
        private const int _ChunkEmbeddingParallelism = 4;
        private static readonly KeyedAsyncLock _MemoryLocks = new KeyedAsyncLock();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _ProvisioningLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly StoreOptions? _StoreOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the memory service.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="embeddingService">The embedding service, required for embedding-based stores.</param>
        /// <param name="storeOptions">Options used to configure external stores (RecallDB, Verbex).</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public MemoryService(DatabaseDriverBase database, EmbeddingService? embeddingService = null, StoreOptions? storeOptions = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _EmbeddingService = embeddingService;
            _StoreOptions = storeOptions;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create or update a memory, storing its content in the scope's memory store and indexing it. If a
        /// memory with the same slug already exists in the (scope, category), it is updated in place.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="category">The owning category.</param>
        /// <param name="incoming">The incoming memory content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persisted memory.</returns>
        public async Task<Memory> UpsertAsync(Scope scope, Category category, Memory incoming, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            // Unpaired surrogates are not valid Unicode: tokenizers throw on them and PostgreSQL cannot store them,
            // so one stray code unit (common in scraped or pasted text) would make the whole memory unstorable.
            incoming.Title = TextSanitizer.ReplaceInvalidSurrogates(incoming.Title);
            incoming.Summary = TextSanitizer.ReplaceInvalidSurrogates(incoming.Summary);
            incoming.Body = TextSanitizer.ReplaceInvalidSurrogates(incoming.Body) ?? incoming.Body;

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("memory upsert", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);

            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);
                await EnsureScopeAsync(store, scope, token).ConfigureAwait(false);

                // Concurrent writes of the same memory would race: both read "no existing row", both clear and
                // recreate the store documents (duplicate document keys), and both insert an index row. Serialize
                // per memory (scope, category, slug); writes to different memories still run in parallel.
                string memoryKey = MemoryLockKey(scope.Id, category.Id, incoming.Slug);
                await _MemoryLocks.WaitAsync(memoryKey, token).ConfigureAwait(false);
                try
                {
                    Memory? existing = await _Database.Memories.ReadBySlugAsync(scope.TenantId, scope.Id, category.Id, incoming.Slug, token).ConfigureAwait(false);
                    Memory target = existing ?? incoming;

                    if (existing != null)
                    {
                        existing.Title = incoming.Title;
                        existing.Summary = incoming.Summary;
                        existing.Body = incoming.Body;
                        existing.Type = incoming.Type;
                        existing.Tags = incoming.Tags;
                        existing.Links = incoming.Links;
                        existing.Metadata = incoming.Metadata;
                        existing.Author = incoming.Author;
                        existing.SessionId = incoming.SessionId;
                        existing.Model = incoming.Model;
                        existing.Version = existing.Version + 1;
                    }
                    else
                    {
                        incoming.TenantId = scope.TenantId;
                        incoming.ScopeId = scope.Id;
                        incoming.CategoryId = category.Id;
                    }

                    IReadOnlyList<MemoryChunk> chunks;
                    if (store.Capabilities.RequiresEmbedding)
                    {
                        ModelEndpoint endpoint = await ResolveEmbeddingEndpointAsync(scope, token).ConfigureAwait(false);
                        string header = ChunkHeader(target);
                        chunks = await MemoryChunker.ChunkAsync(scope, endpoint, target.Body, header, 1.0, token).ConfigureAwait(false);
                        chunks = await EmbedWithBudgetFallbackAsync(scope, endpoint, target.Body, header, chunks, token).ConfigureAwait(false);
                    }
                    else
                    {
                        chunks = new List<MemoryChunk> { new MemoryChunk { Ordinal = 0, Text = target.Body, StartOffset = 0, EndOffset = target.Body.Length } };
                    }

                    target.StoreKey = await store.UpsertAsync(scope, target, chunks, token).ConfigureAwait(false);

                    return existing != null
                        ? await _Database.Memories.UpdateAsync(target, token).ConfigureAwait(false)
                        : await _Database.Memories.CreateAsync(target, token).ConfigureAwait(false);
                }
                finally
                {
                    _MemoryLocks.Release(memoryKey);
                }
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.MemoryUpsertDuration.Record(seconds, tags);
                IsisTelemetry.MemoryUpserts.Add(1, tags);
            }
        }

        /// <summary>
        /// Delete a memory from both the index and the store.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="memory">The memory to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the memory existed and was deleted.</returns>
        public async Task<bool> DeleteAsync(Scope scope, Memory memory, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("memory delete", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagOperation, "memory");

            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);
                string memoryKey = MemoryLockKey(scope.Id, memory.CategoryId, memory.Slug);
                await _MemoryLocks.WaitAsync(memoryKey, token).ConfigureAwait(false);
                try
                {
                    await store.DeleteAsync(scope, memory, token).ConfigureAwait(false);
                    return await _Database.Memories.DeleteAsync(scope.TenantId, memory.Id, token).ConfigureAwait(false);
                }
                finally
                {
                    _MemoryLocks.Release(memoryKey);
                }
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordMemoryDelete("memory", telemetryStart, telemetryOutcome);
            }
        }

        /// <summary>
        /// Cascade-delete a scope: tear down its external store content (RecallDB collection / filesystem
        /// files) and batch-delete all of its memory and category index rows, then delete the scope row.
        /// </summary>
        /// <param name="scope">The scope to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("memory delete_scope", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagOperation, "scope");

            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);
                await store.DeleteScopeAsync(scope, token).ConfigureAwait(false);

                while (true)
                {
                    EnumerationResult<Memory> page = await _Database.Memories.EnumerateAsync(scope.TenantId, scope.Id, null, new EnumerationQuery { MaxResults = 500 }, token).ConfigureAwait(false);
                    if (page.Objects.Count == 0) break;
                    await _Database.Memories.DeleteManyAsync(scope.TenantId, page.Objects.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
                }

                while (true)
                {
                    EnumerationResult<Category> page = await _Database.Categories.EnumerateAsync(scope.TenantId, scope.Id, new EnumerationQuery { MaxResults = 500 }, token).ConfigureAwait(false);
                    if (page.Objects.Count == 0) break;
                    await _Database.Categories.DeleteManyAsync(scope.TenantId, page.Objects.Select(c => c.Id).ToList(), token).ConfigureAwait(false);
                }

                await _Database.Instructions.DeleteByScopeAsync(scope.TenantId, scope.Id, token).ConfigureAwait(false);
                await _Database.Scopes.DeleteAsync(scope.TenantId, scope.Id, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordMemoryDelete("scope", telemetryStart, telemetryOutcome);
            }
        }

        /// <summary>
        /// Tear down any tenant-level external store container after a tenant's scopes have been deleted. Only
        /// RecallDB maintains a tenant-level container (the RecallDB tenant that Isis provisions on first use);
        /// filesystem and Verbex keep no such state. Best-effort: a missing container or unconfigured store is a
        /// no-op so the tenant cascade is never blocked. Virtual to allow the cascade to be observed in tests.
        /// </summary>
        /// <param name="tenantId">The tenant whose external container is being removed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual async Task DeleteTenantStoreAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (_StoreOptions == null || string.IsNullOrEmpty(_StoreOptions.RecallDbEndpoint) || string.IsNullOrEmpty(_StoreOptions.RecallDbAdminKey)) return;

            IMemoryStore store = new RecallDbMemoryStore(_StoreOptions.RecallDbEndpoint!, _StoreOptions.RecallDbAdminKey!);
            await store.DeleteTenantAsync(tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Cascade-delete a category: delete each of its memories from the store and index, then the category
        /// row. Returns false when the category has no memories and the caller should still delete the row.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="categoryId">The category identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteCategoryMemoriesAsync(Scope scope, string categoryId, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (string.IsNullOrEmpty(categoryId)) throw new ArgumentNullException(nameof(categoryId));

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("memory delete_category", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagOperation, "category");

            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);

                while (true)
                {
                    EnumerationResult<Memory> page = await _Database.Memories.EnumerateAsync(scope.TenantId, scope.Id, categoryId, new EnumerationQuery { MaxResults = 500 }, token).ConfigureAwait(false);
                    if (page.Objects.Count == 0) break;

                    foreach (Memory memory in page.Objects)
                    {
                        try
                        {
                            await store.DeleteAsync(scope, memory, token).ConfigureAwait(false);
                        }
                        catch (NotSupportedException)
                        {
                            // Best-effort store cleanup during cascade (e.g. Verbex not wired).
                        }
                    }

                    await _Database.Memories.DeleteManyAsync(scope.TenantId, page.Objects.Select(m => m.Id).ToList(), token).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordMemoryDelete("category", telemetryStart, telemetryOutcome);
            }
        }

        /// <summary>
        /// Search a scope's memory content.
        /// </summary>
        /// <param name="scope">The scope to search.</param>
        /// <param name="query">The search query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The search result.</returns>
        public async Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(query.QueryText)) throw new ArgumentException("A search requires queryText.", nameof(query));

            string mode = query.Mode.ToString();
            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            int telemetryHits = -1;
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("memory search", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagSearchMode, mode);

            try
            {
                IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);

                // Stores label documents with the category id; callers (agents especially) usually know the
                // name. Resolve either form to the id so a name filter does not silently match nothing.
                string? categoryId = await ResolveCategoryFilterAsync(scope, query.CategoryFilter, token).ConfigureAwait(false);
                if (!string.Equals(categoryId, query.CategoryFilter, StringComparison.Ordinal))
                {
                    query = new MemorySearchQuery
                    {
                        QueryText = query.QueryText,
                        Mode = query.Mode,
                        CategoryFilter = categoryId,
                        TopK = query.TopK,
                        TokenBudget = query.TokenBudget,
                        TextWeight = query.TextWeight,
                        MinScore = query.MinScore,
                        RecencyWeight = query.RecencyWeight
                    };
                }

                float[]? queryEmbedding = null;
                if (store.Capabilities.RequiresEmbedding && query.Mode != Isis.Core.Enums.SearchModeEnum.Keyword)
                {
                    queryEmbedding = await EmbedAsync(scope, query.QueryText, token).ConfigureAwait(false);
                }

                MemorySearchResult result = await store.SearchAsync(scope, query, queryEmbedding, token).ConfigureAwait(false);
                if (query.MinScore.HasValue && result.Hits != null)
                {
                    double minimum = query.MinScore.Value;
                    result.Hits = result.Hits.Where(h => h.Score >= minimum).ToList();
                }

                telemetryHits = result.Hits != null ? result.Hits.Count : 0;
                return result;
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { IsisTelemetry.TagSearchMode, mode }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.MemorySearchDuration.Record(seconds, tags);
                IsisTelemetry.MemorySearches.Add(1, tags);
                if (telemetryHits >= 0)
                    IsisTelemetry.MemorySearchResults.Record(telemetryHits, new TagList { { IsisTelemetry.TagSearchMode, mode } });
            }
        }

        /// <summary>
        /// Enumerate a scope's indexed memories (optionally filtered to a category), most useful as a
        /// fallback when relevance search matches nothing but the caller still needs the scope's contents —
        /// for example to summarize "what memories exist?". Reads the memory index directly, so it works
        /// for every store type (including keyword-only filesystem stores).
        /// </summary>
        /// <param name="scope">The scope whose memories to list.</param>
        /// <param name="categoryId">Optional category filter.</param>
        /// <param name="maxResults">Maximum number of memories to return (clamped to 1..1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The memories, newest indexing order as returned by the driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        public async Task<List<Memory>> EnumerateAsync(Scope scope, string? categoryId, int maxResults, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            EnumerationQuery query = new EnumerationQuery { MaxResults = maxResults < 1 ? 1 : maxResults };
            EnumerationResult<Memory> result = await _Database.Memories.EnumerateAsync(scope.TenantId, scope.Id, categoryId, query, token).ConfigureAwait(false);
            return result.Objects ?? new List<Memory>();
        }

        /// <summary>
        /// Enumerate a scope's categories.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="maxResults">Maximum number of categories to return (clamped to 1..1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The categories.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        public async Task<List<Category>> EnumerateCategoriesAsync(Scope scope, int maxResults, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            EnumerationQuery query = new EnumerationQuery { MaxResults = maxResults < 1 ? 1 : maxResults };
            EnumerationResult<Category> result = await _Database.Categories.EnumerateAsync(scope.TenantId, scope.Id, query, token).ConfigureAwait(false);
            return result.Objects ?? new List<Category>();
        }

        /// <summary>
        /// Get the capabilities of the store backing a scope (for example whether it supports semantic search),
        /// without performing any I/O against the store.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The store capabilities.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        public StoreCapabilities GetCapabilities(Scope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            return MemoryStoreFactory.Create(scope, _StoreOptions).Capabilities;
        }

        #endregion

        #region Private-Methods

        private static void RecordMemoryDelete(string operation, long startTimestamp, string outcome)
        {
            double seconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
            TagList tags = new TagList { { IsisTelemetry.TagOperation, operation }, { IsisTelemetry.TagOutcome, outcome } };
            IsisTelemetry.MemoryDeleteDuration.Record(seconds, tags);
            IsisTelemetry.MemoryDeletes.Add(1, tags);
        }

        private async Task EnsureScopeAsync(IMemoryStore store, Scope scope, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(scope.RecallCollectionId))
            {
                await store.EnsureScopeAsync(scope, token).ConfigureAwait(false);
                return;
            }

            // First write to a scope provisions its store collection. Concurrent first writes would each create
            // one (splitting the scope's memories across collections, or failing on the store's unique keys), so
            // provisioning is serialized, and a writer that waited adopts the collection the winner persisted
            // instead of creating another. The lock is per tenant rather than per scope: RecallDB derives
            // per-collection index names from the time component of the collection id, so two collections created
            // in the same instant collide and the second loses its indexes. Serializing creation within a tenant
            // keeps creates apart; only a new scope's first write pays for it.
            SemaphoreSlim gate = _ProvisioningLocks.GetOrAdd(scope.TenantId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                Scope? current = await _Database.Scopes.ReadAsync(scope.TenantId, scope.Id, token).ConfigureAwait(false);
                if (current != null && !string.IsNullOrEmpty(current.RecallCollectionId)) scope.RecallCollectionId = current.RecallCollectionId;

                string? before = scope.RecallCollectionId;
                await store.EnsureScopeAsync(scope, token).ConfigureAwait(false);
                if (!string.Equals(before, scope.RecallCollectionId, StringComparison.Ordinal))
                {
                    await _Database.Scopes.UpdateAsync(scope, token).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<string?> ResolveCategoryFilterAsync(Scope scope, string? filter, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(filter)) return null;

            Category? byId = await _Database.Categories.ReadAsync(scope.TenantId, filter, token).ConfigureAwait(false);
            if (byId != null && byId.ScopeId == scope.Id) return byId.Id;

            Category? byName = await _Database.Categories.ReadByNameAsync(scope.TenantId, scope.Id, filter, token).ConfigureAwait(false);
            if (byName != null) return byName.Id;

            // ReadByNameAsync may be case-sensitive depending on the provider collation; fall back to a
            // case-insensitive match over the scope's categories.
            List<Category> categories = await EnumerateCategoriesAsync(scope, 1000, token).ConfigureAwait(false);
            foreach (Category category in categories)
            {
                if (string.Equals(category.Name, filter, StringComparison.OrdinalIgnoreCase)) return category.Id;
            }

            throw new InvalidOperationException("Category '" + filter + "' was not found in this scope (pass a category name or cat_ id).");
        }

        private async Task EmbedChunksAsync(ModelEndpoint endpoint, IReadOnlyList<MemoryChunk> chunks, CancellationToken token)
        {
            if (chunks.Count == 1)
            {
                chunks[0].Embedding = await _EmbeddingService!.EmbedAsync(endpoint, chunks[0].EmbeddingText ?? chunks[0].Text, token).ConfigureAwait(false);
                return;
            }

            // Embed a multi-chunk memory's chunks concurrently (bounded, so one large memory cannot flood the
            // endpoint): upsert latency then scales with chunks / parallelism rather than with the chunk count.
            using SemaphoreSlim gate = new SemaphoreSlim(_ChunkEmbeddingParallelism);
            List<Task> tasks = new List<Task>(chunks.Count);
            foreach (MemoryChunk chunk in chunks)
            {
                tasks.Add(EmbedChunkAsync(endpoint, chunk, gate, token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task EmbedChunkAsync(ModelEndpoint endpoint, MemoryChunk chunk, SemaphoreSlim gate, CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                chunk.Embedding = await _EmbeddingService!.EmbedAsync(endpoint, chunk.EmbeddingText ?? chunk.Text, token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private static string MemoryLockKey(string scopeId, string categoryId, string slug)
        {
            return scopeId + "/" + categoryId + "/" + slug;
        }

        private static string ChunkHeader(Memory memory)
        {
            // Embed each chunk with the memory's title and summary so a chunk from the middle of a long memory still
            // carries what the memory is about. Only the embedding sees the header; the stored chunk text is unchanged.
            string title = memory.Title?.Trim() ?? string.Empty;
            string summary = memory.Summary?.Trim() ?? string.Empty;
            if (title.Length > 0 && summary.Length > 0) return title + ": " + summary;
            return title.Length > 0 ? title : summary;
        }

        private async Task<IReadOnlyList<MemoryChunk>> EmbedWithBudgetFallbackAsync(Scope scope, ModelEndpoint endpoint, string body, string header, IReadOnlyList<MemoryChunk> chunks, CancellationToken token)
        {
            // The local tokenizer can undercount relative to the serving runtime: a few tokens on technical English,
            // but far more on accented text (the WordPiece vocabulary strips diacritics; some runtimes do not). When the
            // endpoint rejects a chunk as too long, re-chunk at progressively smaller fractions of the budget.
            double[] fallbackScales = new double[] { 0.75, 0.5, 0.3 };
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await EmbedChunksAsync(endpoint, chunks, token).ConfigureAwait(false);
                    return chunks;
                }
                catch (InvalidOperationException e) when (IsContextLengthError(e) && attempt < fallbackScales.Length)
                {
                    chunks = await MemoryChunker.ChunkAsync(scope, endpoint, body, header, fallbackScales[attempt], token).ConfigureAwait(false);
                }
            }
        }

        private static bool IsContextLengthError(InvalidOperationException e)
        {
            string message = e.Message ?? string.Empty;
            return message.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("maximum context", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("too many tokens", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<float[]> EmbedAsync(Scope scope, string text, CancellationToken token)
        {
            ModelEndpoint endpoint = await ResolveEmbeddingEndpointAsync(scope, token).ConfigureAwait(false);
            return await _EmbeddingService!.EmbedAsync(endpoint, text, token).ConfigureAwait(false);
        }

        private async Task<ModelEndpoint> ResolveEmbeddingEndpointAsync(Scope scope, CancellationToken token)
        {
            if (_EmbeddingService == null) throw new InvalidOperationException("This scope requires embeddings but no embedding service is configured on the server.");
            if (string.IsNullOrEmpty(scope.EmbeddingEndpointId)) throw new InvalidOperationException("This scope requires embeddings but has no embedding endpoint configured.");

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(scope.TenantId, scope.EmbeddingEndpointId, token).ConfigureAwait(false);
            if (endpoint == null) throw new InvalidOperationException("The scope's configured embedding endpoint was not found.");

            return endpoint;
        }

        #endregion
    }
}
