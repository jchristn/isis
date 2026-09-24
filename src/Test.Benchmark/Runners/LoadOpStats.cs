namespace Test.Benchmark.Runners
{
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Load-test statistics for one operation type at one concurrency level.
    /// </summary>
    public class LoadOpStats
    {
        #region Public-Members

        /// <summary>
        /// Completed operations.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Failed operations.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Operations per second.
        /// </summary>
        public double Throughput { get; set; } = 0.0;

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        #endregion
    }
}
