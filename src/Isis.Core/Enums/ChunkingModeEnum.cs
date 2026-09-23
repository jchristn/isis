namespace Isis.Core.Enums
{
    /// <summary>
    /// When a scope's memory bodies are split into chunks for embedding.
    /// </summary>
    public enum ChunkingModeEnum
    {
        /// <summary>
        /// Chunk only when a body exceeds the embedding model's token budget (default).
        /// </summary>
        OnOverflow,

        /// <summary>
        /// Always chunk, even when a body would fit in a single embedding call.
        /// </summary>
        Always,

        /// <summary>
        /// Never chunk; embed the whole body (writes fail if a body exceeds the model's budget).
        /// </summary>
        Off
    }
}
