namespace Test.Benchmark
{
    using System.Collections.Generic;

    /// <summary>
    /// A timed, parsed chat-with-memory response.
    /// </summary>
    public class ChatResponse : TimedResponse
    {
        #region Public-Members

        /// <summary>
        /// The model's answer.
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// The standalone form the server searched for a follow-up question, when it rewrote one.
        /// </summary>
        public string? StandaloneQuestion { get; set; } = null;

        /// <summary>
        /// Slugs of the memories the server retrieved and grounded the answer on (not necessarily cited).
        /// </summary>
        public List<string> RetrievedSlugs { get; set; } = new List<string>();

        /// <summary>
        /// Retrieval mode the server used.
        /// </summary>
        public string RetrievalMode { get; set; } = string.Empty;

        /// <summary>
        /// Provider-reported time to first token, when available.
        /// </summary>
        public double TimeToFirstTokenMs { get; set; } = 0.0;

        /// <summary>
        /// Provider-reported generation time, when available.
        /// </summary>
        public double GenerationMs { get; set; } = 0.0;

        /// <summary>
        /// Prompt tokens, when reported.
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens, when reported.
        /// </summary>
        public int CompletionTokens { get; set; } = 0;

        #endregion
    }
}
