namespace Test.Benchmark.Metrics
{
    /// <summary>
    /// Server-side time spent in one pipeline stage during a benchmark phase.
    /// </summary>
    public class StageBreakdown
    {
        #region Public-Members

        /// <summary>
        /// Number of stage executions.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Mean duration per execution in milliseconds.
        /// </summary>
        public double MeanMs { get; set; } = 0.0;

        /// <summary>
        /// Total time in the stage in milliseconds.
        /// </summary>
        public double TotalMs { get; set; } = 0.0;

        #endregion
    }
}
