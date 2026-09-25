namespace Isis.Core.Enums
{
    /// <summary>
    /// What a text is being embedded for. Some embedding models are trained with a different task prefix for stored
    /// documents and for search queries.
    /// </summary>
    public enum EmbeddingPurposeEnum
    {
        /// <summary>
        /// Content being stored (a memory chunk).
        /// </summary>
        Document,

        /// <summary>
        /// A search query.
        /// </summary>
        Query
    }
}
