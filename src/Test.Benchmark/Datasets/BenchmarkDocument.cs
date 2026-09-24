namespace Test.Benchmark.Datasets
{
    /// <summary>
    /// A document to ingest as a memory.
    /// </summary>
    public class BenchmarkDocument
    {
        #region Public-Members

        /// <summary>
        /// Document id; used as the memory slug and matched against query relevance labels.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Category name.
        /// </summary>
        public string Category { get; set; } = "general";

        /// <summary>
        /// Optional title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional one-line summary.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// The memory body.
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Optional date (informational; recorded in memory metadata).
        /// </summary>
        public string? Date { get; set; } = null;

        #endregion
    }
}
