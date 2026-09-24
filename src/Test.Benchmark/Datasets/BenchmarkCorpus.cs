namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;

    /// <summary>
    /// A set of documents searched together (one Isis scope) plus the queries asked of it.
    /// </summary>
    public class BenchmarkCorpus
    {
        #region Public-Members

        /// <summary>
        /// Corpus identifier, unique within the dataset.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Categories documents are filed under. Documents naming an unlisted category get it created on demand.
        /// </summary>
        public List<BenchmarkCategory> Categories { get; set; } = new List<BenchmarkCategory>();

        /// <summary>
        /// The documents (each becomes one memory; its id becomes the memory slug).
        /// </summary>
        public List<BenchmarkDocument> Documents { get; set; } = new List<BenchmarkDocument>();

        /// <summary>
        /// The labelled queries.
        /// </summary>
        public List<BenchmarkQuery> Queries { get; set; } = new List<BenchmarkQuery>();

        #endregion
    }
}
