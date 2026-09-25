namespace Isis.Core.Recall
{
    /// <summary>
    /// The task prefixes an embedding model expects in front of stored documents and search queries.
    /// </summary>
    public class EmbeddingPrefix
    {
        #region Public-Members

        /// <summary>
        /// Prefix for stored content. Empty for none.
        /// </summary>
        public string Document { get; set; } = string.Empty;

        /// <summary>
        /// Prefix for search queries. Empty for none.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty prefix pair.
        /// </summary>
        public EmbeddingPrefix()
        {
        }

        /// <summary>
        /// Instantiate a prefix pair.
        /// </summary>
        /// <param name="document">Prefix for stored content.</param>
        /// <param name="query">Prefix for search queries.</param>
        public EmbeddingPrefix(string document, string query)
        {
            Document = document ?? string.Empty;
            Query = query ?? string.Empty;
        }

        #endregion
    }
}
