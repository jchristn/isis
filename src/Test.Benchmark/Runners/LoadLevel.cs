namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Load-test results at one concurrency level.
    /// </summary>
    public class LoadLevel
    {
        #region Public-Members

        /// <summary>
        /// Concurrent closed-loop workers.
        /// </summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>
        /// Measured seconds (after warmup).
        /// </summary>
        public double Seconds { get; set; } = 0.0;

        /// <summary>
        /// Operations per second across all operation types.
        /// </summary>
        public double Throughput { get; set; } = 0.0;

        /// <summary>
        /// Error rate across all operations (0..1).
        /// </summary>
        public double ErrorRate { get; set; } = 0.0;

        /// <summary>
        /// Per-operation statistics.
        /// </summary>
        public Dictionary<string, LoadOpStats> Operations { get; set; } = new Dictionary<string, LoadOpStats>();

        /// <summary>
        /// Server-side stage breakdown.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        /// <summary>
        /// First few error messages.
        /// </summary>
        public List<string> SampleErrors { get; set; } = new List<string>();

        #endregion
    }
}
