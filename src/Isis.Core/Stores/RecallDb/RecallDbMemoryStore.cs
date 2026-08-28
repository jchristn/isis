namespace Isis.Core.Stores.RecallDb
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using global::RecallDb.Sdk;
    using global::RecallDb.Sdk.Models;
    using Isis.Core.Enums;
    using Isis.Core.Models;
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
                throw new InvalidOperationException("RecallDB scope provisioning failed: " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task<string> UpsertAsync(Scope scope, Memory memory, float[]? embedding, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            RecallDbClient client = RequireClient();
            if (embedding == null) throw new InvalidOperationException("RecallDB requires an embedding vector; none was supplied.");
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) throw new InvalidOperationException("The scope has no RecallDB collection; call EnsureScopeAsync first.");

            DocumentRecord document = new DocumentRecord();
            document.DocumentKey = memory.Id;
            document.DocumentId = memory.Slug;
            document.ContentType = "Text";
            document.Content = memory.Body;
            document.Embeddings = embedding.ToList();
            document.Labels = new List<string> { memory.CategoryId };
            document.Tags = new Dictionary<string, string>(memory.Metadata);
            if (!string.IsNullOrEmpty(memory.Title)) document.Tags["title"] = memory.Title!;

            try
            {
                await client.CreateDocumentAsync(scope.TenantId, scope.RecallCollectionId, document, token).ConfigureAwait(false);
            }
            catch (RecallDbException e)
            {
                throw new InvalidOperationException("RecallDB document upsert failed: " + e.Message, e);
            }

            return memory.Id;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            RecallDbClient client = RequireClient();
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) return;

            try
            {
                await client.DeleteDocumentAsync(scope.TenantId, scope.RecallCollectionId, memory.Id, token).ConfigureAwait(false);
            }
            catch (RecallDbException e)
            {
                throw new InvalidOperationException("RecallDB document delete failed: " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task DeleteScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (_Client == null || string.IsNullOrEmpty(scope.RecallCollectionId)) return;

            try
            {
                await _Client.DeleteCollectionAsync(scope.TenantId, scope.RecallCollectionId, token).ConfigureAwait(false);
            }
            catch (RecallDbException)
            {
                // Best-effort teardown during cascade; ignore a missing/failed collection drop.
            }
        }

        /// <inheritdoc />
        public async Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (query == null) throw new ArgumentNullException(nameof(query));
            RecallDbClient client = RequireClient();
            if (string.IsNullOrEmpty(scope.RecallCollectionId)) throw new InvalidOperationException("The scope has no RecallDB collection; call EnsureScopeAsync first.");

            int topK = query.TopK < 1 ? 5 : query.TopK;
            int fetch = Math.Max(topK, 10);
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
                documents = FuseByReciprocalRank(new[] { vectorResult.Documents, textResult.Documents }, topK);
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
                documents = result.Documents != null ? result.Documents.Take(topK).ToList() : new List<DocumentRecord>();
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
                    StoreKey = document.DocumentKey,
                    Slug = document.DocumentId,
                    Title = title,
                    Snippet = snippet,
                    Score = document.Score
                });
            }

            return output;
        }

        #endregion

        #region Private-Methods

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
        /// 1/(k + rank) across the lists it appears in, then return the top results. This blends the vector
        /// and full-text rankings without needing to normalize their score scales.
        /// </summary>
        private static List<DocumentRecord> FuseByReciprocalRank(IEnumerable<List<DocumentRecord>?> lists, int topK)
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
                .Take(topK)
                .Select(pair => byKey[pair.Key])
                .ToList();
        }

        private RecallDbClient RequireClient()
        {
            if (_Client == null) throw new NotSupportedException("This RecallDB store is not configured with an endpoint. Configure the RecallDB endpoint and admin key in settings.");
            return _Client;
        }

        #endregion
    }
}
