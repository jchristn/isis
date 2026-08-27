namespace Isis.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;

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

            IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);
            await EnsureScopeAsync(store, scope, token).ConfigureAwait(false);

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

            float[]? embedding = null;
            if (store.Capabilities.RequiresEmbedding)
            {
                embedding = await EmbedAsync(scope, target.Body, token).ConfigureAwait(false);
            }

            target.StoreKey = await store.UpsertAsync(scope, target, embedding, token).ConfigureAwait(false);

            return existing != null
                ? await _Database.Memories.UpdateAsync(target, token).ConfigureAwait(false)
                : await _Database.Memories.CreateAsync(target, token).ConfigureAwait(false);
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

            IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);
            await store.DeleteAsync(scope, memory, token).ConfigureAwait(false);
            return await _Database.Memories.DeleteAsync(scope.TenantId, memory.Id, token).ConfigureAwait(false);
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

            IMemoryStore store = MemoryStoreFactory.Create(scope, _StoreOptions);

            float[]? queryEmbedding = null;
            if (store.Capabilities.RequiresEmbedding && query.Mode != Isis.Core.Enums.SearchModeEnum.Keyword)
            {
                queryEmbedding = await EmbedAsync(scope, query.QueryText, token).ConfigureAwait(false);
            }

            return await store.SearchAsync(scope, query, queryEmbedding, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task EnsureScopeAsync(IMemoryStore store, Scope scope, CancellationToken token)
        {
            string? before = scope.RecallCollectionId;
            await store.EnsureScopeAsync(scope, token).ConfigureAwait(false);
            if (!string.Equals(before, scope.RecallCollectionId, StringComparison.Ordinal))
            {
                await _Database.Scopes.UpdateAsync(scope, token).ConfigureAwait(false);
            }
        }

        private async Task<float[]> EmbedAsync(Scope scope, string text, CancellationToken token)
        {
            if (_EmbeddingService == null) throw new InvalidOperationException("This scope requires embeddings but no embedding service is configured on the server.");
            if (string.IsNullOrEmpty(scope.EmbeddingEndpointId)) throw new InvalidOperationException("This scope requires embeddings but has no embedding endpoint configured.");

            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(scope.TenantId, scope.EmbeddingEndpointId, token).ConfigureAwait(false);
            if (endpoint == null) throw new InvalidOperationException("The scope's configured embedding endpoint was not found.");

            return await _EmbeddingService.EmbedAsync(endpoint, text, token).ConfigureAwait(false);
        }

        #endregion
    }
}
