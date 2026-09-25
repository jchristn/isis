namespace Isis.Core.Models
{
    /// <summary>
    /// An existing memory that closely resembles one being written, reported back on upsert so the writer can decide
    /// whether the new memory duplicates or replaces it.
    /// </summary>
    public class SimilarMemory
    {
        #region Public-Members

        /// <summary>
        /// Memory id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Memory slug.
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Memory title, when set.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Category id.
        /// </summary>
        public string CategoryId { get; set; } = string.Empty;

        /// <summary>
        /// Vector similarity to the memory being written (cosine similarity for RecallDB scopes, 0 to 1 for typical
        /// text embeddings).
        /// </summary>
        public double Similarity { get; set; } = 0.0;

        #endregion
    }
}
