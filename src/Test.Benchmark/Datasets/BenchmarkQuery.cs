namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A labelled query. An empty <see cref="Relevant"/> list marks a question the corpus cannot answer.
    /// </summary>
    public class BenchmarkQuery
    {
        #region Public-Members

        /// <summary>
        /// Query id, unique within the corpus.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The query text.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Query type used to break results down (for example paraphrase, lexical, multi, negative).
        /// </summary>
        public string Type { get; set; } = "default";

        /// <summary>
        /// Ids of the relevant documents, most important first.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Optional graded relevance (document id to gain). Documents in <see cref="Relevant"/> but absent here have gain 1.
        /// </summary>
        public Dictionary<string, int>? Grades { get; set; } = null;

        /// <summary>
        /// Optional category name to filter the search to.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Optional gold answer, for end-to-end chat evaluation. "NOT_IN_MEMORY" marks an unanswerable question.
        /// </summary>
        public string? Answer { get; set; } = null;

        /// <summary>
        /// Optional date the question is asked on (LongMemEval temporal questions depend on it).
        /// </summary>
        public string? Date { get; set; } = null;

        /// <summary>
        /// Optional earlier messages, oldest first, for a follow-up question that only makes sense in context. The chat
        /// command sends them as the request's history; retrieval commands search the question alone.
        /// </summary>
        public List<BenchmarkTurn>? History { get; set; } = null;

        /// <summary>
        /// True when the corpus contains the answer.
        /// </summary>
        [JsonIgnore]
        public bool Answerable
        {
            get
            {
                return Relevant != null && Relevant.Count > 0;
            }
        }

        #endregion
    }
}
