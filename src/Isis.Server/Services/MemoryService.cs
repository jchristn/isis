namespace Isis.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core;
    using Isis.Core.Database;
    using Isis.Core.Enums;
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
        #region Public-Members

        /// <summary>
        /// Whether an upsert looks for existing memories that closely resemble the one written and reports them in
        /// <c>similarMemories</c>. Only scopes with semantic search (RecallDB) are checked. Default true.
        /// </summary>
        public bool DuplicateCheckEnabled { get; set; } = true;

        /// <summary>
        /// Minimum vector similarity for an existing memory to be reported as similar on upsert, in the range 0.0 to
        /// 1.0. Default 0.85.
        /// </summary>
        public double DuplicateSimilarityThreshold
        {
            get
            {
                return _DuplicateSimilarityThreshold;
            }
            set
            {
                if (value < 0.0 || value > 1.0) throw new ArgumentOutOfRangeException(nameof(DuplicateSimilarityThreshold), "DuplicateSimilarityThreshold must be in [0, 1].");
                _DuplicateSimilarityThreshold = value;
            }
        }

        /// <summary>
        /// Maximum number of similar memories reported on upsert. Minimum 1, maximum 20, default 3.
        /// </summary>
        public int DuplicateMaxResults
        {
            get
            {
                return _DuplicateMaxResults;
            }
            set
            {
                if (value < 1 || value > 20) throw new ArgumentOutOfRangeException(nameof(DuplicateMaxResults), "DuplicateMaxResults must be in [1, 20].");
                _DuplicateMaxResults = value;
            }
        }

        /// <summary>
        /// How many chunks of one memory are embedded at the same time. Minimum 1, maximum 32, default 4. Lower it for
        /// an embedding endpoint that accepts only a few concurrent requests.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [1, 32].</exception>
        public int EmbeddingParallelism
        {
            get
            {
                return _EmbeddingParallelism;
            }
            set
            {
                if (value < 1 || value > 32) throw new ArgumentOutOfRangeException(nameof(EmbeddingParallelism), "EmbeddingParallelism must be between 1 and 32.");
                _EmbeddingParallelism = value;
            }
        }

        /// <summary>
        /// RRF constant used to fuse the rankings of several queries (additional queries or decomposed parts), at least 1.
        /// Default 60. This fuses whole ranked hit lists, not the two legs of one hybrid search, whose constant is
        /// <see cref="HybridFusion.DefaultRrfK"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1.</exception>
        public static int QueryFusionRrfK
        {
            get
            {
                return _QueryFusionRrfK;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(QueryFusionRrfK), "QueryFusionRrfK must be at least 1.");
                _QueryFusionRrfK = value;
            }
        }

        /// <summary>
        /// After a rerank endpoint fails, how long searches skip it (returning retrieval order with a notice) before
        /// trying it again, so an unreachable reranker costs one failed call per period rather than one per search.
        /// Minimum zero (never skip), default 30 seconds.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below zero.</exception>
        public TimeSpan RerankCooldown
        {
            get
            {
                return _RerankCooldown;
            }
            set
            {
                if (value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(RerankCooldown), "RerankCooldown may not be negative.");
                _RerankCooldown = value;
            }
        }

        /// <summary>
        /// Characters of each candidate's text sent to the reranker (the reranker sees the title and this much of the
        /// best-matching chunk). Minimum 100, default 1200.
        /// </summary>
        public int RerankPassageChars
        {
            get
            {
                return _RerankPassageChars;
            }
            set
            {
                if (value < 100) throw new ArgumentOutOfRangeException(nameof(RerankPassageChars), "RerankPassageChars must be at least 100.");
                _RerankPassageChars = value;
            }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly EmbeddingService? _EmbeddingService;
        private readonly LookupCache? _Cache;
        private int _EmbeddingParallelism = 4;
        private static readonly KeyedAsyncLock _MemoryLocks = new KeyedAsyncLock();
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _ProvisioningLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly StoreOptions? _StoreOptions;
        private readonly RerankService? _RerankService;
        private readonly SearchRefiner _Refiner;
        private const int _DefaultSnippetChars = 240;
        private const int _MaxAdditionalQueries = 4;
        private static int _QueryFusionRrfK = 60;
        private double _DuplicateSimilarityThreshold = 0.85;
        private int _DuplicateMaxResults = 3;
        private int _RerankPassageChars = 1200;
        private TimeSpan _RerankCooldown = TimeSpan.FromSeconds(30);
        private static readonly ConcurrentDictionary<string, DateTime> _RerankSkipUntil = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the memory service.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="embeddingService">The embedding service, required for embedding-based stores.</param>
        /// <param name="storeOptions">Options used to configure external stores (RecallDB, Verbex).</param>
        /// <param name="cache">Optional lookup cache for embedding-endpoint reads; also invalidated when provisioning fills in a scope's collection id.</param>
        /// <param name="rerankService">Optional rerank service, required to rerank searches in scopes with a rerank endpoint.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public MemoryService(DatabaseDriverBase database, EmbeddingService? embeddingService = null, StoreOptions? storeOptions = null, LookupCache? cache = null, RerankService? rerankService = null)
        {
            _Cache = cache;
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _EmbeddingService = embeddingService;
            _StoreOptions = storeOptions;
            _RerankService = rerankService;
            _Refiner = new SearchRefiner(_Database);
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
                    List<string> supersedes = NormalizeSupersedes(incoming, existing);

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
                        existing.Supersedes = supersedes;
                        existing.Version = existing.Version + 1;
                    }
                    else
                    {
                        incoming.TenantId = scope.TenantId;
                        incoming.ScopeId = scope.Id;
                        incoming.CategoryId = category.Id;
                        incoming.Supersedes = supersedes;
                        incoming.SupersededBy = null;
                    }

                    incoming.SimilarMemories = null;

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

                    Memory saved = existing != null
                        ? await _Database.Memories.UpdateAsync(target, token).ConfigureAwait(false)
                        : await _Database.Memories.CreateAsync(target, token).ConfigureAwait(false);

                    await ApplySupersessionAsync(scope, saved, existing == null, token).ConfigureAwait(false);
                    if (DuplicateCheckEnabled && store.Capabilities.SupportsSemantic && chunks.Count > 0 && chunks[0].Embedding != null)
                    {
                        saved.SimilarMemories = await FindSimilarAsync(store, scope, saved, chunks[0].Embedding!, token).ConfigureAwait(false);
                    }

                    return saved;
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
                    bool deleted = await _Database.Memories.DeleteAsync(scope.TenantId, memory.Id, token).ConfigureAwait(false);

                    // Memories this one replaced become current again.
                    List<Memory> replaced = await _Database.Memories.ReadSupersededByAsync(scope.TenantId, memory.Id, token).ConfigureAwait(false);
                    if (replaced.Count > 0) await _Database.Memories.SetSupersededByAsync(scope.TenantId, replaced.Select(m => m.Id).ToList(), null, token).ConfigureAwait(false);
                    return deleted;
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
                query = query.Clone();
                query.CategoryFilter = categoryId;

                // Reranking and diversity choose from a wider candidate pool than topK, and the reranker reads more
                // of each candidate than the caller's snippet budget; both are cut back after.
                ModelEndpoint? rerankEndpoint = await ResolveRerankEndpointAsync(scope, query, token).ConfigureAwait(false);
                string? rerankNotice = null;
                if (rerankEndpoint != null && query.Rerank != true && _RerankSkipUntil.TryGetValue(rerankEndpoint.Id, out DateTime skipUntil) && skipUntil > DateTime.UtcNow)
                {
                    rerankEndpoint = null;
                    rerankNotice = "The rerank endpoint failed recently and is being skipped; results are in retrieval order.";
                }
                int topK = query.TopK;
                int snippetChars = query.TokenBudget.HasValue && query.TokenBudget.Value > 0 ? query.TokenBudget.Value : _DefaultSnippetChars;
                MemorySearchQuery storeQuery = query.Clone();
                if (rerankEndpoint != null)
                {
                    storeQuery.TopK = Math.Max(topK, scope.RerankCandidates);
                    storeQuery.TokenBudget = Math.Max(snippetChars, _RerankPassageChars);
                }

                if (query.Diversity > 0.0) storeQuery.TopK = Math.Max(storeQuery.TopK, Math.Min(100, topK * 3));

                // Hybrid fusion settings the caller left unset come from the embedding model's profile (then the
                // store's generic defaults).
                bool embed = store.Capabilities.RequiresEmbedding && query.Mode != SearchModeEnum.Keyword;
                if (store.Capabilities.RequiresEmbedding)
                {
                    ModelEndpoint embeddingEndpoint = await ResolveEmbeddingEndpointAsync(scope, token).ConfigureAwait(false);
                    EmbeddingModelProfile? modelProfile = EmbeddingModelProfiles.Find(embeddingEndpoint.Model);
                    if (!storeQuery.TextWeight.HasValue) storeQuery.TextWeight = modelProfile?.TextWeight;
                    if (!storeQuery.RrfK.HasValue) storeQuery.RrfK = modelProfile?.RrfK;
                }

                List<string> queries = QueryTexts(query);
                MemorySearchResult result;
                if (queries.Count == 1)
                {
                    float[]? queryEmbedding = embed ? await EmbedAsync(scope, query.QueryText, token, EmbeddingPurposeEnum.Query).ConfigureAwait(false) : null;
                    result = await store.SearchAsync(scope, storeQuery, queryEmbedding, token).ConfigureAwait(false);
                }
                else
                {
                    // Search each part on its own, then fuse the rankings, so a memory that answers one part of a
                    // multi-part question is not crowded out by memories that answer another.
                    List<Task<MemorySearchResult>> searches = new List<Task<MemorySearchResult>>(queries.Count);
                    foreach (string text in queries) searches.Add(SearchOneAsync(store, scope, storeQuery, text, embed, token));
                    MemorySearchResult[] results = await Task.WhenAll(searches).ConfigureAwait(false);
                    result = results[0];
                    result.Hits = FuseQueryResults(results.Select(r => r.Hits ?? new List<MemorySearchHit>()).ToList());
                }

                List<MemorySearchHit> hits = result.Hits ?? new List<MemorySearchHit>();
                if (query.MinScore.HasValue)
                {
                    double minimum = query.MinScore.Value;
                    hits = hits.Where(h => h.Score >= minimum).ToList();
                }

                if (rerankEndpoint != null && hits.Count > 0)
                {
                    // A reranker outage degrades the search to retrieval order rather than failing it.
                    try
                    {
                        hits = await RerankAsync(rerankEndpoint, query.QueryText, hits, token).ConfigureAwait(false);
                        result.Reranked = true;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
                    {
                        result.Notice = "Reranking failed (" + e.Message + "); results are in retrieval order.";
                        if (_RerankCooldown > TimeSpan.Zero) _RerankSkipUntil[rerankEndpoint.Id] = DateTime.UtcNow.Add(_RerankCooldown);
                    }

                    double? minRerank = query.MinRerankScore ?? scope.RerankMinScore;
                    if (result.Reranked && minRerank.HasValue)
                    {
                        double minimum = minRerank.Value;
                        hits = hits.Where(h => h.Score >= minimum).ToList();
                    }
                }

                if (rerankNotice != null) result.Notice = string.IsNullOrEmpty(result.Notice) ? rerankNotice : result.Notice + " " + rerankNotice;
                hits = SearchDiversifier.Diversify(hits, query.Diversity, topK);
                foreach (MemorySearchHit hit in hits)
                {
                    if (hit.Snippet != null && hit.Snippet.Length > snippetChars + 1) hit.Snippet = hit.Snippet.Substring(0, snippetChars) + "\u2026";
                }

                result.Hits = await _Refiner.RefineAsync(scope, hits, query.Superseded, query.LinkExpansion, topK, snippetChars, token).ConfigureAwait(false);
                telemetryHits = result.Hits.Count;
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
                    _Cache?.InvalidateScope(scope.TenantId, scope.Id);
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

        private async Task<MemorySearchResult> SearchOneAsync(IMemoryStore store, Scope scope, MemorySearchQuery storeQuery, string text, bool embed, CancellationToken token)
        {
            MemorySearchQuery part = storeQuery.Clone();
            part.QueryText = text;
            part.AdditionalQueries = null;
            float[]? embedding = embed ? await EmbedAsync(scope, text, token, EmbeddingPurposeEnum.Query).ConfigureAwait(false) : null;
            return await store.SearchAsync(scope, part, embedding, token).ConfigureAwait(false);
        }

        private static List<string> QueryTexts(MemorySearchQuery query)
        {
            List<string> texts = new List<string> { query.QueryText };
            foreach (string extra in query.AdditionalQueries ?? new List<string>())
            {
                string trimmed = (extra ?? string.Empty).Trim();
                if (trimmed.Length == 0 || texts.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) continue;
                texts.Add(trimmed);
                if (texts.Count > _MaxAdditionalQueries) break;
            }

            return texts;
        }

        /// <summary>
        /// Fuse the ranked hits of several queries by reciprocal rank (k = <see cref="QueryFusionRrfK"/>), normalized so a memory ranked first by
        /// every query scores 1.0. Each memory keeps the hit (snippet and evidence) from the query that ranked it best.
        /// </summary>
        /// <param name="rankings">One ranked hit list per query.</param>
        /// <returns>The fused hits, best first.</returns>
        public static List<MemorySearchHit> FuseQueryResults(List<List<MemorySearchHit>> rankings)
        {
            int k = QueryFusionRrfK;
            Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, MemorySearchHit> best = new Dictionary<string, MemorySearchHit>(StringComparer.Ordinal);
            Dictionary<string, int> bestRank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (List<MemorySearchHit> ranking in rankings)
            {
                for (int i = 0; i < ranking.Count; i++)
                {
                    MemorySearchHit hit = ranking[i];
                    string key = hit.StoreKey + "|" + hit.Slug;
                    scores[key] = (scores.TryGetValue(key, out double score) ? score : 0.0) + 1.0 / (k + i + 1);
                    if (!bestRank.TryGetValue(key, out int rank) || i < rank)
                    {
                        bestRank[key] = i;
                        best[key] = hit;
                    }
                }
            }

            double top = rankings.Count / (double)(k + 1);
            return scores
                .OrderByDescending(e => e.Value)
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .Select(e =>
                {
                    MemorySearchHit hit = best[e.Key];
                    hit.Score = top > 0.0 ? e.Value / top : 0.0;
                    return hit;
                })
                .ToList();
        }

        private static List<string> NormalizeSupersedes(Memory incoming, Memory? existing)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in incoming.Supersedes ?? new List<string>())
            {
                string value = (entry ?? string.Empty).Trim();
                if (value.Length == 0) continue;
                if (string.Equals(value, incoming.Slug, StringComparison.Ordinal)) continue;
                if (existing != null && string.Equals(value, existing.Id, StringComparison.Ordinal)) continue;
                if (seen.Add(value)) result.Add(value);
            }

            return result;
        }

        private async Task ApplySupersessionAsync(Scope scope, Memory saved, bool created, CancellationToken token)
        {
            // Mark the memories this one names as replaced, and release any it named before but no longer does.
            List<Memory> targets = new List<Memory>();
            if (saved.Supersedes.Count > 0)
            {
                List<string> ids = saved.Supersedes.Where(s => s.StartsWith(Constants.MemoryPrefix, StringComparison.Ordinal)).ToList();
                targets.AddRange(await _Database.Memories.ReadBySlugsAsync(scope.TenantId, scope.Id, saved.Supersedes, token).ConfigureAwait(false));
                if (ids.Count > 0)
                {
                    List<Memory> byIds = await _Database.Memories.ReadManyAsync(scope.TenantId, ids, token).ConfigureAwait(false);
                    targets.AddRange(byIds.Where(m => string.Equals(m.ScopeId, scope.Id, StringComparison.Ordinal)));
                }

                targets = targets.Where(m => !string.Equals(m.Id, saved.Id, StringComparison.Ordinal)).GroupBy(m => m.Id).Select(g => g.First()).ToList();
                List<string> toMark = targets.Where(m => !string.Equals(m.SupersededBy, saved.Id, StringComparison.Ordinal)).Select(m => m.Id).ToList();
                if (toMark.Count > 0) await _Database.Memories.SetSupersededByAsync(scope.TenantId, toMark, saved.Id, token).ConfigureAwait(false);
            }

            if (!created)
            {
                HashSet<string> current = new HashSet<string>(targets.Select(m => m.Id), StringComparer.Ordinal);
                List<Memory> previous = await _Database.Memories.ReadSupersededByAsync(scope.TenantId, saved.Id, token).ConfigureAwait(false);
                List<string> released = previous.Where(m => !current.Contains(m.Id)).Select(m => m.Id).ToList();
                if (released.Count > 0) await _Database.Memories.SetSupersededByAsync(scope.TenantId, released, null, token).ConfigureAwait(false);
            }
            else
            {
                // A replacement written before the memory it replaces (for example on re-import) already names it.
                List<Memory> superseders = await _Database.Memories.ReadSupersedingAsync(scope.TenantId, scope.Id, saved.Slug, token).ConfigureAwait(false);
                Memory? superseder = superseders.Where(m => !string.Equals(m.Id, saved.Id, StringComparison.Ordinal)).OrderByDescending(m => m.CreatedUtc).FirstOrDefault();
                if (superseder != null)
                {
                    await _Database.Memories.SetSupersededByAsync(scope.TenantId, new List<string> { saved.Id }, superseder.Id, token).ConfigureAwait(false);
                    saved.SupersededBy = superseder.Id;
                }
            }
        }

        private async Task<List<SimilarMemory>?> FindSimilarAsync(IMemoryStore store, Scope scope, Memory saved, float[] embedding, CancellationToken token)
        {
            // Best effort: the memory is already written, so a failed lookup only means no similarity report.
            try
            {
                MemorySearchQuery query = new MemorySearchQuery
                {
                    QueryText = saved.Title ?? saved.Slug,
                    Mode = SearchModeEnum.Semantic,
                    TopK = DuplicateMaxResults + 1,
                    TokenBudget = 1
                };

                MemorySearchResult result = await store.SearchAsync(scope, query, embedding, token).ConfigureAwait(false);
                List<MemorySearchHit> close = (result.Hits ?? new List<MemorySearchHit>())
                    .Where(h => !string.Equals(h.StoreKey, saved.StoreKey, StringComparison.Ordinal) && !string.Equals(h.StoreKey, saved.Id, StringComparison.Ordinal))
                    .Where(h => (h.VectorScore ?? h.Score) >= _DuplicateSimilarityThreshold)
                    .Take(DuplicateMaxResults)
                    .ToList();
                if (close.Count == 0) return null;

                List<Memory> memories = await _Database.Memories.ReadBySlugsAsync(scope.TenantId, scope.Id, close.Where(h => h.Slug != null).Select(h => h.Slug!).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false);
                List<SimilarMemory> similar = new List<SimilarMemory>();
                foreach (MemorySearchHit hit in close)
                {
                    Memory? memory = memories.FirstOrDefault(m => string.Equals(m.StoreKey, hit.StoreKey, StringComparison.Ordinal) || string.Equals(m.Id, hit.StoreKey, StringComparison.Ordinal))
                        ?? memories.FirstOrDefault(m => string.Equals(m.Slug, hit.Slug, StringComparison.Ordinal));
                    if (memory == null || string.Equals(memory.Id, saved.Id, StringComparison.Ordinal)) continue;
                    if (string.Equals(memory.SupersededBy, saved.Id, StringComparison.Ordinal)) continue;

                    similar.Add(new SimilarMemory
                    {
                        Id = memory.Id,
                        Slug = memory.Slug,
                        Title = memory.Title,
                        CategoryId = memory.CategoryId,
                        Similarity = Math.Round(hit.VectorScore ?? hit.Score, 4)
                    });
                }

                return similar.Count > 0 ? similar : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private async Task<ModelEndpoint?> ResolveRerankEndpointAsync(Scope scope, MemorySearchQuery query, CancellationToken token)
        {
            if (query.Rerank == false) return null;
            if (string.IsNullOrEmpty(scope.RerankEndpointId))
            {
                if (query.Rerank == true) throw new InvalidOperationException("Reranking was requested but this scope has no rerank endpoint configured.");
                return null;
            }

            if (_RerankService == null) throw new InvalidOperationException("This scope reranks results but no rerank service is configured on the server.");

            ModelEndpoint? endpoint = _Cache != null
                ? await _Cache.GetEndpointAsync(scope.TenantId, scope.RerankEndpointId, token).ConfigureAwait(false)
                : await _Database.ModelEndpoints.ReadAsync(scope.TenantId, scope.RerankEndpointId, token).ConfigureAwait(false);
            if (endpoint == null) throw new InvalidOperationException("The scope's configured rerank endpoint was not found.");
            if (!endpoint.Active)
            {
                if (query.Rerank == true) throw new InvalidOperationException("The scope's rerank endpoint is inactive.");
                return null;
            }

            return endpoint;
        }

        private async Task<List<MemorySearchHit>> RerankAsync(ModelEndpoint endpoint, string queryText, List<MemorySearchHit> hits, CancellationToken token)
        {
            List<string> passages = hits
                .Select(h => (string.IsNullOrEmpty(h.Title) ? string.Empty : h.Title + "\n") + (h.Snippet ?? string.Empty))
                .Select(p => p.Length > _RerankPassageChars ? p.Substring(0, _RerankPassageChars) : p)
                .ToList();
            double[] scores = await _RerankService!.RerankAsync(endpoint, queryText, passages, token).ConfigureAwait(false);

            for (int i = 0; i < hits.Count; i++)
            {
                hits[i].RerankScore = scores[i];
                hits[i].Score = scores[i];
            }

            // Stable: ties keep the retrieval order.
            return hits.Select((h, i) => new KeyValuePair<int, MemorySearchHit>(i, h))
                .OrderByDescending(p => p.Value.Score)
                .ThenBy(p => p.Key)
                .Select(p => p.Value)
                .ToList();
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
            using SemaphoreSlim gate = new SemaphoreSlim(_EmbeddingParallelism);
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

        private async Task<float[]> EmbedAsync(Scope scope, string text, CancellationToken token, EmbeddingPurposeEnum purpose)
        {
            ModelEndpoint endpoint = await ResolveEmbeddingEndpointAsync(scope, token).ConfigureAwait(false);
            return await _EmbeddingService!.EmbedAsync(endpoint, text, token, purpose).ConfigureAwait(false);
        }

        private async Task<ModelEndpoint> ResolveEmbeddingEndpointAsync(Scope scope, CancellationToken token)
        {
            if (_EmbeddingService == null) throw new InvalidOperationException("This scope requires embeddings but no embedding service is configured on the server.");
            if (string.IsNullOrEmpty(scope.EmbeddingEndpointId)) throw new InvalidOperationException("This scope requires embeddings but has no embedding endpoint configured.");

            ModelEndpoint? endpoint = _Cache != null
                ? await _Cache.GetEndpointAsync(scope.TenantId, scope.EmbeddingEndpointId, token).ConfigureAwait(false)
                : await _Database.ModelEndpoints.ReadAsync(scope.TenantId, scope.EmbeddingEndpointId, token).ConfigureAwait(false);
            if (endpoint == null) throw new InvalidOperationException("The scope's configured embedding endpoint was not found.");

            return endpoint;
        }

        #endregion
    }
}
