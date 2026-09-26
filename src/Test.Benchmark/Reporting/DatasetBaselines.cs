namespace Test.Benchmark.Reporting
{
    using System.Collections.Generic;

    /// <summary>
    /// The published baselines for one dataset, or a note explaining why there are none.
    /// </summary>
    public class DatasetBaselines
    {
        #region Public-Members

        /// <summary>
        /// Context for the baselines, or why none apply.
        /// </summary>
        public string Note { get; set; } = string.Empty;

        /// <summary>
        /// The published baselines.
        /// </summary>
        public List<PublishedBaseline> Baselines { get; set; } = new List<PublishedBaseline>();

        #endregion
    }
}
