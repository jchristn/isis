namespace Isis.Core.Stores.Verbex
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// A memory store backed by Verbex, providing keyword / TF-IDF retrieval. No embeddings; no semantic
    /// or hybrid search.
    /// </summary>
    /// <remarks>
    /// The Verbex integration is wired in a later build phase. This type advertises its capabilities but
    /// throws when invoked so that misconfiguration is explicit.
    /// </remarks>
    public class VerbexMemoryStore : IMemoryStore
    {
        #region Public-Members

        /// <inheritdoc />
        public StoreCapabilities Capabilities { get; } = new StoreCapabilities
        {
            SupportsKeyword = true,
            SupportsSemantic = false,
            SupportsHybrid = false,
            RequiresEmbedding = false,
            Description = "Verbex inverted index. Keyword / TF-IDF search only; no embeddings."
        };

        #endregion

        #region Private-Members

        private const string _NotWired = "The Verbex memory store is not yet wired in this build. Configure a filesystem-backed scope, or use a build with Verbex integration enabled.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a Verbex memory store.
        /// </summary>
        public VerbexMemoryStore()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task EnsureScopeAsync(Scope scope, CancellationToken token = default)
        {
            throw new NotSupportedException(_NotWired);
        }

        /// <inheritdoc />
        public Task<string> UpsertAsync(Scope scope, Memory memory, float[]? embedding, CancellationToken token = default)
        {
            throw new NotSupportedException(_NotWired);
        }

        /// <inheritdoc />
        public Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default)
        {
            throw new NotSupportedException(_NotWired);
        }

        /// <inheritdoc />
        public Task DeleteScopeAsync(Scope scope, CancellationToken token = default)
        {
            // Best-effort teardown during cascade: no persistent Verbex content to remove in this build.
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token = default)
        {
            throw new NotSupportedException(_NotWired);
        }

        #endregion
    }
}
