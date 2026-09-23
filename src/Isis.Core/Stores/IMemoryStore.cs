namespace Isis.Core.Stores
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// A memory store backend that holds memory content and performs retrieval for a scope. Implementations
    /// include RecallDB (semantic/hybrid), Verbex (keyword), and the filesystem (flat files).
    /// </summary>
    public interface IMemoryStore
    {
        /// <summary>
        /// The retrieval capabilities of this provider.
        /// </summary>
        StoreCapabilities Capabilities { get; }

        /// <summary>
        /// Ensure any backing structures for a scope exist (for example, a RecallDB collection or a target directory).
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task EnsureScopeAsync(Scope scope, CancellationToken token = default);

        /// <summary>
        /// Create or update a memory's content in the store. The body is supplied as one or more chunks: a
        /// small memory is a single chunk carrying the whole body, while an oversized memory is split into
        /// ordinal chunks that each fit the embedding budget. Embedding-based providers store one document per
        /// chunk (sharing the parent memory's identity) so that retrieval can roll chunks back up to a single
        /// hit per memory; providers that do not embed may treat the memory as a whole and ignore the split.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="memory">The memory whose body is being stored.</param>
        /// <param name="chunks">The ordered body chunks, each carrying its embedding when the provider requires one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The store key identifying the stored content (the parent memory key).</returns>
        Task<string> UpsertAsync(Scope scope, Memory memory, IReadOnlyList<MemoryChunk> chunks, CancellationToken token = default);

        /// <summary>
        /// Delete a memory's content from the store.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="memory">The memory to remove.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default);

        /// <summary>
        /// Tear down all backing content for a scope (for example, drop the RecallDB collection or remove the
        /// target directory/files). Best-effort: implementations should not throw for a missing backing store,
        /// so that cascading deletes are not blocked.
        /// </summary>
        /// <param name="scope">The scope whose content is being removed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteScopeAsync(Scope scope, CancellationToken token = default);

        /// <summary>
        /// Tear down any tenant-level container this provider maintains (for example, the RecallDB tenant that
        /// holds a tenant's collections). Called once when a tenant is deleted, after its scopes have already
        /// been torn down. Best-effort: implementations must not throw for a missing/absent container (or when
        /// the provider keeps no tenant-level state), so that cascading deletes are not blocked.
        /// </summary>
        /// <param name="tenantId">The tenant whose backing container is being removed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteTenantAsync(string tenantId, CancellationToken token = default);

        /// <summary>
        /// Search the scope's memory content.
        /// </summary>
        /// <param name="scope">The scope to search.</param>
        /// <param name="query">The search query.</param>
        /// <param name="queryEmbedding">The query embedding vector, when the provider uses one; otherwise null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The search result.</returns>
        Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token = default);
    }
}
