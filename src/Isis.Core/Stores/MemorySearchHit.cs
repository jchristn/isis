namespace Isis.Core.Stores
{
    /// <summary>
    /// A single ranked result from a memory store search.
    /// </summary>
    public class MemorySearchHit
    {
        #region Public-Members

        /// <summary>
        /// The store key of the matched memory content.
        /// </summary>
        public string StoreKey { get; set; } = string.Empty;

        /// <summary>
        /// The memory slug, when known.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// The memory title, when known.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// A relevance-bearing snippet of the memory body.
        /// </summary>
        public string Snippet { get; set; } = string.Empty;

        /// <summary>
        /// The blended relevance score. Higher is more relevant.
        /// </summary>
        public double Score { get; set; } = 0.0;

        /// <summary>
        /// Vector (semantic) similarity for this hit, when the store ran a vector search and returned it. Null otherwise.
        /// </summary>
        public double? VectorScore { get; set; } = null;

        /// <summary>
        /// Full-text relevance for this hit, when the store ran a text search and returned it. Null otherwise.
        /// </summary>
        public double? TextScore { get; set; } = null;

        /// <summary>
        /// 1-based rank in the vector leg of a hybrid search. Null when not ranked there or not hybrid.
        /// </summary>
        public int? VectorRank { get; set; } = null;

        /// <summary>
        /// 1-based rank in the text leg of a hybrid search. Null when not ranked there or not hybrid.
        /// </summary>
        public int? TextRank { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory search hit.
        /// </summary>
        public MemorySearchHit()
        {
        }

        #endregion
    }
}
