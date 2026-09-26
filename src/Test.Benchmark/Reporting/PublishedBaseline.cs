namespace Test.Benchmark.Reporting
{
    using System.Collections.Generic;

    /// <summary>
    /// A published result for a dataset, used to put Isis's score in context.
    /// </summary>
    public class PublishedBaseline
    {
        #region Public-Members

        /// <summary>
        /// The system the result is for, for example "BM25".
        /// </summary>
        public string System { get; set; } = string.Empty;

        /// <summary>
        /// The metric name as the harness reports it, for example "ndcg@10".
        /// </summary>
        public string Metric { get; set; } = "ndcg@10";

        /// <summary>
        /// The published value.
        /// </summary>
        public double Value { get; set; } = 0.0;

        /// <summary>
        /// The Isis search modes this baseline is compared against (for example Hybrid and Keyword for BM25).
        /// </summary>
        public List<string> CompareModes { get; set; } = new List<string> { "Hybrid" };

        /// <summary>
        /// Where the number comes from.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Link to the source.
        /// </summary>
        public string? Url { get; set; } = null;

        #endregion
    }
}
