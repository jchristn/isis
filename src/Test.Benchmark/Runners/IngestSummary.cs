namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Ingest (memory upsert) statistics for one or more corpora.
    /// </summary>
    public class IngestSummary
    {
        #region Public-Members

        /// <summary>
        /// Documents upserted in this run (0 when every scope was reused).
        /// </summary>
        public int Documents { get; set; } = 0;

        /// <summary>
        /// Upserts that failed.
        /// </summary>
        public int Failures { get; set; } = 0;

        /// <summary>
        /// Scopes reused from a previous run without re-ingesting.
        /// </summary>
        public int ReusedScopes { get; set; } = 0;

        /// <summary>
        /// Wall-clock seconds spent ingesting.
        /// </summary>
        public double WallSeconds { get; set; } = 0.0;

        /// <summary>
        /// Documents per second (wall clock).
        /// </summary>
        public double DocumentsPerSecond { get; set; } = 0.0;

        /// <summary>
        /// Upsert concurrency used.
        /// </summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>
        /// Per-upsert client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stage breakdown during ingest.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        /// <summary>
        /// First few failure messages.
        /// </summary>
        public List<string> SampleErrors { get; set; } = new List<string>();

        #endregion
    }
}
