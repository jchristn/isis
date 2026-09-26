namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of one chat-with-memory question.
    /// </summary>
    public class ChatItem
    {
        #region Public-Members

        /// <summary>
        /// Corpus id.
        /// </summary>
        public string Corpus { get; set; } = string.Empty;

        /// <summary>
        /// Query id.
        /// </summary>
        public string QueryId { get; set; } = string.Empty;

        /// <summary>
        /// Query type.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Question asked.
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Gold answer.
        /// </summary>
        public string Gold { get; set; } = string.Empty;

        /// <summary>
        /// Model answer (thinking stripped).
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// Judge verdict (null when not judged or unparseable).
        /// </summary>
        public bool? Correct { get; set; } = null;

        /// <summary>
        /// HTTP status of the chat call.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Error text, when the call failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// The standalone form the server searched for a follow-up question, when it rewrote one.
        /// </summary>
        public string? StandaloneQuestion { get; set; } = null;

        /// <summary>
        /// Client latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0.0;

        /// <summary>
        /// Relevant slugs.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Slugs the server retrieved into the prompt.
        /// </summary>
        public List<string> Retrieved { get; set; } = new List<string>();

        /// <summary>
        /// Slugs the answer cited in [brackets] that exist in the corpus.
        /// </summary>
        public List<string> Cited { get; set; } = new List<string>();

        #endregion
    }
}
