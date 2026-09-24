namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of one query in one search mode.
    /// </summary>
    public class QueryOutcome
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
        /// Requested search mode.
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// Mode the server reported using.
        /// </summary>
        public string EffectiveMode { get; set; } = string.Empty;

        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Client latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0.0;

        /// <summary>
        /// Ranked slugs returned.
        /// </summary>
        public List<string> Ranked { get; set; } = new List<string>();

        /// <summary>
        /// Relevant slugs.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Score of the top hit (0 when there were no hits).
        /// </summary>
        public double TopScore { get; set; } = 0.0;

        /// <summary>
        /// Highest raw vector similarity among the hits (0 when none reported one).
        /// </summary>
        public double TopVectorScore { get; set; } = 0.0;

        /// <summary>
        /// Metric name to value for this query (empty for unanswerable queries).
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        #endregion
    }
}
