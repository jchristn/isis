namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Aggregate retrieval results for one search mode.
    /// </summary>
    public class ModeSummary
    {
        #region Public-Members

        /// <summary>
        /// Search mode.
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// Answerable queries scored.
        /// </summary>
        public int Queries { get; set; } = 0;

        /// <summary>
        /// Unanswerable (negative) queries run.
        /// </summary>
        public int NegativeQueries { get; set; } = 0;

        /// <summary>
        /// Failed requests.
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Queries where the server used a different mode than requested.
        /// </summary>
        public int ModeMismatches { get; set; } = 0;

        /// <summary>
        /// Mean metric values over answerable queries.
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Mean metric values per query type.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByType { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Mean top-hit score for answerable queries.
        /// </summary>
        public double MeanTopScoreAnswerable { get; set; } = 0.0;

        /// <summary>
        /// Mean top-hit score for unanswerable queries. Close to the answerable mean means scores cannot be used
        /// as a relevance threshold to say "nothing relevant".
        /// </summary>
        public double MeanTopScoreNegative { get; set; } = 0.0;

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stage breakdown for this mode's queries.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        #endregion
    }
}
