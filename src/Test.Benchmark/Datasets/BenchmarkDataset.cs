namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;

    /// <summary>
    /// A provider-neutral benchmark dataset: one or more corpora, each ingested into its own Isis scope and
    /// queried with labelled questions.
    /// </summary>
    public class BenchmarkDataset
    {
        #region Public-Members

        /// <summary>
        /// Short dataset name, used in scope names and result files.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The corpora. Most datasets have one; LongMemEval has one per question (each question has its own haystack).
        /// </summary>
        public List<BenchmarkCorpus> Corpora { get; set; } = new List<BenchmarkCorpus>();

        #endregion
    }
}
