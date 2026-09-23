namespace Isis.Core.Stores.RecallDb
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using global::RecallDb.Sdk;
    using global::RecallDb.Sdk.Models;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Observability;
    using Isis.Core.Stores;

    /// <summary>
    /// A memory store backed by RecallDB, providing vector, full-text, and hybrid retrieval. RecallDB is
    /// bring-your-own-vector: embeddings are computed by Isis and supplied on write and query. Isis maps a
    /// tenant to a RecallDB tenant, a scope to a collection, a category to a label, and a memory to a document.
    /// </summary>
    public class RecallDbMemoryStore : IMemoryStore
    {
        #region Public-Members

        /// <inheritdoc />
        public StoreCapabilities Capabilities { get; } = new StoreCapabilities
        {
            SupportsKeyword = true,
            SupportsSemantic = true,
            SupportsHybrid = true,
            RequiresEmbedding = true,
            Description = "RecallDB (Postgres + pgvector). Vector, full-text, and hybrid search; requires an embedding endpoint."
        };

        #endregion

        #region Private-Members

        private readonly RecallDbClient? _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an unconfigured store (capabilities only; operations throw until configured).
        /// </summary>
        public RecallDbMemoryStore()
        {
        }

        /// <summary>
        /// Instantiate a configured RecallDB store.
        /// </summary>
        /// <param name="endpoint">The RecallDB server endpoint.</param>
        /// <param name="adminKey">The RecallDB admin/bearer key.</param>
        /// <exception cref="ArgumentException">Thrown when endpoint or key is missing.</exception>
        public RecallDbMemoryStore(string endpoint, string adminKey)
        {
            if (string.IsNullOrEmpty(endpoint)) throw new ArgumentException("A RecallDB endpoint is required.", nameof(endpoint));
            if (string.IsNullOrEmpty(adminKey)) throw new ArgumentException("A RecallDB admin key is required.", nameof(adminKey));
            _Client = new RecallDbClient(endpoint, adminKey);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EnsureScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            RecallDbClient client = RequireClient();
            if (scope.Dimensionality <= 0) throw new InvalidOperationException("A RecallDB scope requires a positive embedding dimensionality (configure the scope's embedding endpoint).");

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store ensure_scope", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "ensure_scope");
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);

            try
            {
                bool tenantExists = await client.TenantExistsAsync(scope.TenantId, token).ConfigureAwait(false);
                if (!tenantExists)
                {
                    await client.CreateTenantAsync(new TenantMetadata { Id = scope.TenantId, Name = scope.TenantId, Active = true }, token).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(scope.RecallCollectionId))
                {
                    CollectionMetadata created = await client.CreateCollectionAsync(scope.TenantId, new CollectionMetadata
                    {
                        TenantId = scope.TenantId,
                        Name = scope.Id,
                        Description = scope.Name,
                        Dimensionality = scope.Dimensionality,
                        Active = true
                    }, token).ConfigureAwait(false);
                    scope.RecallCollectionId = created.Id;
                }
            }
            catch (RecallDbException e)
            {
                telemetryOutcome = "error";
                InvalidOperationException wrapped = new InvalidOperationException("RecallDB scope provisioning failed: " + e.Message, e);
                IsisTelemetry.RecordException(activity, wrapped);
                throw wrapped;
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordStoreOp("ensure_scope", telemetryStart, telemetryOutcome);
            }
        }

        /// <inheritdoc />
        public async Task<string> UpsertAsync(Scope scope, Memory memory, IReadOnlyList<MemoryChunk> chunks, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (chunks == null || chunks.Count == 0) throw new InvalidOperationException("RecallDB requires at least one chunk; none was supplied.");
            RecallDbClient client = RequireClient();
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) throw new InvalidOperationException("The scope has no RecallDB collection; call EnsureScopeAsync first.");

            bool single = chunks.Count == 1;
            List<DocumentRecord> documents = new List<DocumentRecord>(chunks.Count);
            foreach (MemoryChunk chunk in chunks)
            {
                if (chunk.Embedding == null) throw new InvalidOperationException("RecallDB requires an embedding vector for every chunk; chunk " + chunk.Ordinal + " has none.");

                DocumentRecord document = new DocumentRecord();
                document.DocumentKey = single ? memory.Id : ChunkDocumentKey(memory.Id, chunk.Ordinal);
                document.DocumentId = memory.Slug;
                document.Position = chunk.Ordinal;
                document.ContentType = "Text";
                document.Content = chunk.Text;
                document.Embeddings = chunk.Embedding.ToList();
                document.Labels = new List<string> { memory.CategoryId };
                document.Tags = new Dictionary<string, string>(memory.Metadata);
                document.Tags["parentKey"] = memory.Id;
                document.Tags["ordinal"] = chunk.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(memory.Title)) document.Tags["title"] = memory.Title!;
                documents.Add(document);
            }

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store upsert", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "upsert");
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag("isis.chunks", chunks.Count);

            try
            {
                // Clear any prior documents for this memory so an update (which may re-chunk into a different
                // count) never leaves orphaned chunk documents behind.
                await DeleteByParentAsync(client, scope, memory.Id, token).ConfigureAwait(false);

                if (single)
                {
                    await client.CreateDocumentAsync(scope.TenantId, scope.RecallCollectionId, documents[0], token).ConfigureAwait(false);
                }
                else
                {
                    await client.CreateDocumentBatchAsync(scope.TenantId, scope.RecallCollectionId, documents, token).ConfigureAwait(false);
                }

                return memory.Id;
            }
            catch (RecallDbException e)
            {
                telemetryOutcome = "error";
                InvalidOperationException wrapped = new InvalidOperationException("RecallDB document upsert failed: " + e.Message, e);
                IsisTelemetry.RecordException(activity, wrapped);
                throw wrapped;
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
                TagList tags = new TagList { { IsisTelemetry.TagStore, "recalldb" }, { IsisTelemetry.TagOperation, "upsert" }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.StoreUpsertDuration.Record(seconds, tags);
                IsisTelemetry.StoreUpserts.Add(1, tags);
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            RecallDbClient client = RequireClient();
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) return;

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store delete", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "delete");
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);

            try
            {
                await DeleteByParentAsync(client, scope, memory.Id, token).ConfigureAwait(false);
            }
            catch (RecallDbException e)
            {
                telemetryOutcome = "error";
                InvalidOperationException wrapped = new InvalidOperationException("RecallDB document delete failed: " + e.Message, e);
                IsisTelemetry.RecordException(activity, wrapped);
                throw wrapped;
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordStoreOp("delete", telemetryStart, telemetryOutcome);
            }
        }

        /// <inheritdoc />
        public async Task DeleteScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (_Client == null || string.IsNullOrEmpty(scope.RecallCollectionId)) return;

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store delete_scope", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "delete_scope");
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);

            try
            {
                await _Client.DeleteCollectionAsync(scope.TenantId, scope.RecallCollectionId, token).ConfigureAwait(false);
            }
            catch (RecallDbException)
            {
                // Best-effort teardown during cascade; ignore a missing/failed collection drop.
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordStoreOp("delete_scope", telemetryStart, telemetryOutcome);
            }
        }

        /// <inheritdoc />
        public async Task DeleteTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (_Client == null) return;

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store delete_tenant", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "delete_tenant");

            try
            {
                // Isis provisions a RecallDB tenant on first use; drop it once the tenant's collections are gone.
                if (await _Client.TenantExistsAsync(tenantId, token).ConfigureAwait(false))
                {
                    await _Client.DeleteTenantAsync(tenantId, token).ConfigureAwait(false);
                }
            }
            catch (RecallDbException)
            {
                // Best-effort teardown during cascade; ignore a missing/failed tenant drop.
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                RecordStoreOp("delete_tenant", telemetryStart, telemetryOutcome);
            }
        }

        /// <inheritdoc />
        public async Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (query == null) throw new ArgumentNullException(nameof(query));
            RecallDbClient client = RequireClient();
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) throw new InvalidOperationException("The scope has no RecallDB collection; call EnsureScopeAsync first.");

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            int telemetryHits = -1;
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("store search", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagStore, "recalldb");
            activity?.SetTag(IsisTelemetry.TagOperation, "search");
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagSearchMode, query.Mode.ToString());

            try
            {
            int topK = query.TopK < 1 ? 5 : query.TopK;
            // Fetch well beyond topK: a single memory may contribute several chunk documents that collapse to
            // one hit, so we need enough raw documents to still surface topK distinct memories after rollup.
            int fetch = Math.Max(topK * 4, 20);
            bool wantSemantic = queryEmbedding != null && query.Mode != SearchModeEnum.Keyword;
            bool wantKeyword = query.Mode != SearchModeEnum.Semantic && !string.IsNullOrEmpty(query.QueryText);

            LabelFilter? labelFilter = !string.IsNullOrEmpty(query.CategoryFilter)
                ? new LabelFilter { Required = new List<string> { query.CategoryFilter! } }
                : null;

            List<DocumentRecord> documents;
            SearchModeEnum effectiveMode;

            if (wantSemantic && wantKeyword)
            {
                // Hybrid = UNION of a vector-only and a full-text-only search, fused by reciprocal rank.
                // RecallDB's combined Vector+FullText query applies the text query as a REQUIRED filter, so a
                // strong vector match is dropped whenever a natural-language question shares no literal keyword
                // with the memory (the common case for "what can you tell me about X?"). Running the two
                // searches separately and unioning them keeps semantic hits that have no keyword overlap.
                SearchQuery vectorQuery = new SearchQuery
                {
                    MaxResults = fetch,
                    LabelFilter = labelFilter,
                    Vector = new VectorQuery { SearchType = "CosineSimilarity", Embeddings = queryEmbedding!.ToList() }
                };
                SearchQuery textQuery = new SearchQuery
                {
                    MaxResults = fetch,
                    LabelFilter = labelFilter,
                    FullText = new FullTextQuery { Query = query.QueryText, TextWeight = query.TextWeight }
                };

                SearchResult vectorResult = await ExecuteSearchAsync(client, scope, vectorQuery, token).ConfigureAwait(false);
                SearchResult textResult = await ExecuteSearchAsync(client, scope, textQuery, token).ConfigureAwait(false);
                // Fuse the two rankings at the chunk-document level, then roll chunks up to one hit per memory.
                List<DocumentRecord> fused = FuseByReciprocalRank(new[] { vectorResult.Documents, textResult.Documents });
                documents = GroupByParent(fused, topK);
                effectiveMode = SearchModeEnum.Hybrid;
            }
            else
            {
                SearchQuery single = new SearchQuery { MaxResults = fetch, LabelFilter = labelFilter };
                if (wantSemantic)
                {
                    single.Vector = new VectorQuery { SearchType = "CosineSimilarity", Embeddings = queryEmbedding!.ToList() };
                    effectiveMode = SearchModeEnum.Semantic;
                }
                else
                {
                    single.FullText = new FullTextQuery { Query = query.QueryText, TextWeight = query.TextWeight };
                    effectiveMode = SearchModeEnum.Keyword;
                }

                SearchResult result = await ExecuteSearchAsync(client, scope, single, token).ConfigureAwait(false);
                documents = GroupByParent(result.Documents, topK);
            }

            MemorySearchResult output = new MemorySearchResult();
            output.EffectiveMode = effectiveMode;

            int budget = query.TokenBudget.HasValue && query.TokenBudget.Value > 0 ? query.TokenBudget.Value : 240;
            foreach (DocumentRecord document in documents)
            {
                string content = document.Content ?? string.Empty;
                string snippet = content.Length > budget ? content.Substring(0, budget) + "…" : content;
                string? title = document.Tags != null && document.Tags.TryGetValue("title", out string? titleValue) ? titleValue : null;

                output.Hits.Add(new MemorySearchHit
                {
                    StoreKey = ParentKey(document),
                    Slug = document.DocumentId,
                    Title = title,
                    Snippet = snippet,
                    Score = document.Score
                });
            }

            telemetryHits = output.Hits.Count;
            activity?.SetTag("isis.hits", telemetryHits);
            return output;
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
                TagList tags = new TagList { { IsisTelemetry.TagStore, "recalldb" }, { IsisTelemetry.TagOperation, "search" }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.StoreSearchDuration.Record(seconds, tags);
                IsisTelemetry.StoreSearches.Add(1, tags);
            }
        }

        #endregion

        #region Private-Methods

        private static void RecordStoreOp(string operation, long startTimestamp, string outcome)
        {
            double seconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
            TagList tags = new TagList { { IsisTelemetry.TagStore, "recalldb" }, { IsisTelemetry.TagOperation, operation }, { IsisTelemetry.TagOutcome, outcome } };
            IsisTelemetry.StoreOpDuration.Record(seconds, tags);
            IsisTelemetry.StoreOps.Add(1, tags);
        }

        private static async Task<SearchResult> ExecuteSearchAsync(RecallDbClient client, Scope scope, SearchQuery search, CancellationToken token)
        {
            try
            {
                return await client.SearchAsync(scope.TenantId, scope.RecallCollectionId, search, token).ConfigureAwait(false);
            }
            catch (RecallDbException e)
            {
                throw new InvalidOperationException("RecallDB search failed: " + e.Message, e);
            }
        }

        /// <summary>
        /// Reciprocal-rank fusion: union multiple ranked result lists, scoring each document by the sum of
        /// 1/(k + rank) across the lists it appears in, then return every document ordered by fused score. This
        /// blends the vector and full-text rankings without needing to normalize their score scales; the caller
        /// rolls the fused chunk documents up to memories and truncates to topK.
        /// </summary>
        private static List<DocumentRecord> FuseByReciprocalRank(IEnumerable<List<DocumentRecord>?> lists)
        {
            const int k = 60;
            Dictionary<string, DocumentRecord> byKey = new Dictionary<string, DocumentRecord>();
            Dictionary<string, double> scores = new Dictionary<string, double>();

            foreach (List<DocumentRecord>? list in lists)
            {
                if (list == null) continue;
                for (int rank = 0; rank < list.Count; rank++)
                {
                    DocumentRecord document = list[rank];
                    string key = document.DocumentKey ?? document.DocumentId ?? (rank + ":" + (document.Content ?? string.Empty).GetHashCode());
                    if (!byKey.ContainsKey(key)) byKey[key] = document;
                    scores[key] = (scores.TryGetValue(key, out double existing) ? existing : 0.0) + 1.0 / (k + rank + 1);
                }
            }

            return scores
                .OrderByDescending(pair => pair.Value)
                .Select(pair => byKey[pair.Key])
                .ToList();
        }

        /// <summary>
        /// Collapse chunk documents to at most <paramref name="topK"/> memories, keeping the best-scoring chunk
        /// per parent memory. The input must already be ordered best-first; the first document seen for a parent
        /// wins and represents that memory (its content becomes the hit snippet).
        /// </summary>
        private static List<DocumentRecord> GroupByParent(IEnumerable<DocumentRecord>? ordered, int topK)
        {
            List<DocumentRecord> result = new List<DocumentRecord>();
            if (ordered == null) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DocumentRecord document in ordered)
            {
                string parent = ParentKey(document);
                if (!seen.Add(parent)) continue;
                result.Add(document);
                if (result.Count >= topK) break;
            }

            return result;
        }

        /// <summary>
        /// Resolve the parent memory key for a document: the <c>parentKey</c> tag written on upsert, falling
        /// back to the document key (single-chunk / legacy documents whose key is the memory id).
        /// </summary>
        private static string ParentKey(DocumentRecord document)
        {
            if (document.Tags != null && document.Tags.TryGetValue("parentKey", out string? parent) && !string.IsNullOrEmpty(parent)) return parent;
            return document.DocumentKey ?? document.DocumentId ?? string.Empty;
        }

        /// <summary>
        /// Build the store document key for a chunk of a memory. The separator MUST stay URL-safe (RFC 3986
        /// unreserved): the RecallDB SDK places the document key straight into the request path without
        /// encoding, so a URL-reserved character such as '#' (fragment) or '?' (query) would silently truncate
        /// the delete/exists path and orphan the chunk documents. Uses "-c" (both characters unreserved) and the
        /// memory id (a PrettyId of unreserved characters), so the composed key is always URL-safe.
        /// </summary>
        internal static string ChunkDocumentKey(string memoryId, int ordinal)
        {
            return memoryId + "-c" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Delete every stored document belonging to a memory: the single/legacy document keyed by the memory
        /// id, plus any ordinal chunk documents keyed by <see cref="ChunkDocumentKey"/>. Chunk documents are
        /// written with contiguous ordinals, so probing ordinals in order until one is absent removes them all
        /// without needing to know the chunk count.
        /// </summary>
        private static async Task DeleteByParentAsync(RecallDbClient client, Scope scope, string memoryId, CancellationToken token)
        {
            if (await client.DocumentExistsAsync(scope.TenantId, scope.RecallCollectionId, memoryId, token).ConfigureAwait(false))
            {
                await client.DeleteDocumentAsync(scope.TenantId, scope.RecallCollectionId, memoryId, token).ConfigureAwait(false);
            }

            for (int ordinal = 0; ; ordinal++)
            {
                string key = ChunkDocumentKey(memoryId, ordinal);
                if (!await client.DocumentExistsAsync(scope.TenantId, scope.RecallCollectionId, key, token).ConfigureAwait(false)) break;
                await client.DeleteDocumentAsync(scope.TenantId, scope.RecallCollectionId, key, token).ConfigureAwait(false);
            }
        }

        private RecallDbClient RequireClient()
        {
            if (_Client == null) throw new NotSupportedException("This RecallDB store is not configured with an endpoint. Configure the RecallDB endpoint and admin key in settings.");
            return _Client;
        }

        #endregion
    }
}
