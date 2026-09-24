namespace Isis.Core.Stores
{
    /// <summary>
    /// One embeddable piece of a memory body. A small memory produces a single chunk carrying the whole body;
    /// an oversized memory is split into several ordinal chunks that each fit the embedding model's token
    /// budget. Each chunk becomes its own store document (sharing the parent memory's identity), and retrieval
    /// rolls chunks back up to a single hit per memory.
    /// </summary>
    public class MemoryChunk
    {
        #region Public-Members

        /// <summary>
        /// Zero-based position of this chunk within its parent memory. A single-chunk memory uses ordinal 0.
        /// </summary>
        public int Ordinal { get; set; } = 0;

        /// <summary>
        /// The chunk text that is embedded and stored. Never null.
        /// </summary>
        public string Text
        {
            get
            {
                return _Text;
            }
            set
            {
                _Text = value ?? string.Empty;
            }
        }

        /// <summary>
        /// The text sent to the embedding model for this chunk, when it differs from <see cref="Text"/>: the memory's
        /// title and summary header followed by the chunk text. Null means embed <see cref="Text"/> as is. The store
        /// always persists <see cref="Text"/>, so snippets and full-text search are unaffected by the header.
        /// </summary>
        public string? EmbeddingText { get; set; } = null;

        /// <summary>
        /// The embedding vector for this chunk, when the store requires one; otherwise null.
        /// </summary>
        public float[]? Embedding { get; set; } = null;

        /// <summary>
        /// The character offset of this chunk's start within the full memory body.
        /// </summary>
        public int StartOffset { get; set; } = 0;

        /// <summary>
        /// The character offset of this chunk's end (exclusive) within the full memory body.
        /// </summary>
        public int EndOffset { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _Text = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory chunk.
        /// </summary>
        public MemoryChunk()
        {
        }

        #endregion
    }
}
