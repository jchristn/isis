namespace Isis.Core.Stores
{
    /// <summary>
    /// Describes the retrieval capabilities of a memory store provider, so callers and the UI can
    /// communicate what is and is not possible for a given scope.
    /// </summary>
    public class StoreCapabilities
    {
        #region Public-Members

        /// <summary>
        /// Whether keyword / full-text search is supported.
        /// </summary>
        public bool SupportsKeyword { get; set; } = true;

        /// <summary>
        /// Whether semantic (vector) search is supported.
        /// </summary>
        public bool SupportsSemantic { get; set; } = false;

        /// <summary>
        /// Whether hybrid (vector + lexical) search is supported.
        /// </summary>
        public bool SupportsHybrid { get; set; } = false;

        /// <summary>
        /// Whether the provider requires an embedding vector to be supplied on write and query.
        /// </summary>
        public bool RequiresEmbedding { get; set; } = false;

        /// <summary>
        /// A short human-readable description of the provider's capabilities.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate store capabilities.
        /// </summary>
        public StoreCapabilities()
        {
        }

        #endregion
    }
}
