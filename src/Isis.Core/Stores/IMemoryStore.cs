namespace Isis.Core.Stores
{
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
        /// Create or update a memory's content in the store.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="memory">The memory whose body is being stored.</param>
        /// <param name="embedding">The embedding vector, when the provider requires one; otherwise null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The store key identifying the stored content.</returns>
        Task<string> UpsertAsync(Scope scope, Memory memory, float[]? embedding, CancellationToken token = default);

        /// <summary>
        /// Delete a memory's content from the store.
        /// </summary>
        /// <param name="scope">The owning scope.</param>
        /// <param name="memory">The memory to remove.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default);

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
