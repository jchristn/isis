namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Full result of a chat-with-memory (RAG) benchmark run.
    /// </summary>
    public class ChatReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind discriminator.
        /// </summary>
        public string Kind { get; set; } = "chat";

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// Run environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Run configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Headline metrics (accuracy, abstention accuracy, citation precision/recall, context recall, ...).
        /// </summary>
        public Dictionary<string, double> Summary { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Metrics per query type.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByType { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stage breakdown.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        /// <summary>
        /// Every item.
        /// </summary>
        public List<ChatItem> Items { get; set; } = new List<ChatItem>();

        #endregion
    }
}
