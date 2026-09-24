namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Generic;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// A corpus ingested into an Isis scope.
    /// </summary>
    public class ProvisionedScope
    {
        #region Public-Members

        /// <summary>
        /// The corpus.
        /// </summary>
        public BenchmarkCorpus Corpus { get; set; } = new BenchmarkCorpus();

        /// <summary>
        /// The scope id.
        /// </summary>
        public string ScopeId { get; set; } = string.Empty;

        /// <summary>
        /// Category name to id.
        /// </summary>
        public Dictionary<string, string> Categories { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
