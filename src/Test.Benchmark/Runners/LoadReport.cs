namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// Full result of a load benchmark run.
    /// </summary>
    public class LoadReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind discriminator.
        /// </summary>
        public string Kind { get; set; } = "load";

        /// <summary>
        /// Scenario name.
        /// </summary>
        public string Scenario { get; set; } = string.Empty;

        /// <summary>
        /// Run environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Run configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Memories in the scope under test (before load writes).
        /// </summary>
        public int CorpusSize { get; set; } = 0;

        /// <summary>
        /// Corpus preload statistics.
        /// </summary>
        public IngestSummary Ingest { get; set; } = new IngestSummary();

        /// <summary>
        /// Results per concurrency level.
        /// </summary>
        public List<LoadLevel> Levels { get; set; } = new List<LoadLevel>();

        #endregion
    }
}
