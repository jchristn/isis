namespace Test.Benchmark.Runners
{
    /// <summary>
    /// One timed operation recorded during a load test.
    /// </summary>
    internal class LoadSample
    {
        #region Public-Members

        /// <summary>
        /// Operation name.
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// Client latency in milliseconds.
        /// </summary>
        public double ElapsedMs { get; set; } = 0.0;

        /// <summary>
        /// True when the call succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Error text for a failed call.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
