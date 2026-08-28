namespace Isis.Core.Recall
{
    /// <summary>
    /// A single streamed chunk from an inference endpoint: an incremental slice of visible answer content
    /// and/or reasoning ("thinking") content, plus token counts on the terminal chunk.
    /// </summary>
    public class InferenceChunk
    {
        #region Public-Members

        /// <summary>
        /// Incremental visible answer text in this chunk, if any.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Incremental reasoning/thinking text in this chunk, if any (from a provider reasoning channel).
        /// </summary>
        public string? Reasoning { get; set; } = null;

        /// <summary>
        /// Whether this is the terminal chunk of the stream.
        /// </summary>
        public bool Done { get; set; } = false;

        /// <summary>
        /// Prompt (input) token count, reported on the terminal chunk when the endpoint provides it.
        /// </summary>
        public int? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion (output) token count, reported on the terminal chunk when the endpoint provides it.
        /// </summary>
        public int? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Time to first token in milliseconds, reported on the terminal chunk when available.
        /// </summary>
        public double? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Generation time in milliseconds (first token to last), reported on the terminal chunk when available.
        /// </summary>
        public double? GenerationMs { get; set; } = null;

        /// <summary>
        /// Output tokens per second over the generation window, reported on the terminal chunk when available.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an inference chunk.
        /// </summary>
        public InferenceChunk()
        {
        }

        #endregion
    }
}
