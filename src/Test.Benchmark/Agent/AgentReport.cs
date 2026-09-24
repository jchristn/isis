namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Full result of an agent benchmark run.
    /// </summary>
    public class AgentReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind discriminator.
        /// </summary>
        public string Kind { get; set; } = "agent";

        /// <summary>
        /// Suite name.
        /// </summary>
        public string Suite { get; set; } = string.Empty;

        /// <summary>
        /// Run environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Run configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Per-arm headline metrics (successRate, meanTurns, meanCostUsd, meanDurationMs).
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> Arms { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Every item.
        /// </summary>
        public List<AgentItem> Items { get; set; } = new List<AgentItem>();

        #endregion
    }
}
