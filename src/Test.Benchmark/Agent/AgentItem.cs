namespace Test.Benchmark.Agent
{
    /// <summary>
    /// The result of one task in one arm.
    /// </summary>
    public class AgentItem
    {
        #region Public-Members

        /// <summary>
        /// Task id.
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// Task type.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Arm (isis or none).
        /// </summary>
        public string Arm { get; set; } = string.Empty;

        /// <summary>
        /// True when every expectation matched and no forbidden pattern did.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Agent turns.
        /// </summary>
        public int Turns { get; set; } = 0;

        /// <summary>
        /// Reported cost in USD.
        /// </summary>
        public double CostUsd { get; set; } = 0.0;

        /// <summary>
        /// Wall-clock milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0.0;

        /// <summary>
        /// Final answer text (truncated).
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// Error text when the run failed.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
