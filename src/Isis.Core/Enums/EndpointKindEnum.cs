namespace Isis.Core.Enums
{
    /// <summary>
    /// The kind of AI model endpoint being configured.
    /// </summary>
    public enum EndpointKindEnum
    {
        /// <summary>
        /// An embedding endpoint that turns text into a vector. Used to write and query RecallDB scopes.
        /// </summary>
        Embedding,

        /// <summary>
        /// An inference/completion endpoint. Used for memory hygiene (summaries, dedup, compaction)
        /// and the chat-with-memory surface.
        /// </summary>
        Inference,

        /// <summary>
        /// A reranking endpoint (a cross-encoder) that scores how well each candidate passage answers a query. Used
        /// to reorder and threshold search results after retrieval.
        /// </summary>
        Rerank
    }
}
