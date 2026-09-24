namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// Full result of a retrieval benchmark run.
    /// </summary>
    public class RetrievalReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind discriminator.
        /// </summary>
        public string Kind { get; set; } = "retrieval";

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// Dataset description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Run environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Run configuration (argument name to value).
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Ingest statistics.
        /// </summary>
        public IngestSummary Ingest { get; set; } = new IngestSummary();

        /// <summary>
        /// Per-mode results.
        /// </summary>
        public List<ModeSummary> Modes { get; set; } = new List<ModeSummary>();

        /// <summary>
        /// Every query outcome (for drill-down and diffs).
        /// </summary>
        public List<QueryOutcome> Outcomes { get; set; } = new List<QueryOutcome>();

        #endregion
    }
}
