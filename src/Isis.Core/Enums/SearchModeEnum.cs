namespace Isis.Core.Enums
{
    /// <summary>
    /// The retrieval strategy requested for a memory search. Availability depends on the scope's store provider.
    /// </summary>
    public enum SearchModeEnum
    {
        /// <summary>
        /// Keyword/full-text retrieval. Available on all providers.
        /// </summary>
        Keyword,

        /// <summary>
        /// Vector/semantic retrieval. Available on RecallDB scopes only.
        /// </summary>
        Semantic,

        /// <summary>
        /// Hybrid retrieval blending vector and lexical relevance. Available on RecallDB scopes only.
        /// </summary>
        Hybrid
    }
}
