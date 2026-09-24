namespace Test.Benchmark
{
    /// <summary>
    /// One search hit.
    /// </summary>
    public class SearchHit
    {
        #region Public-Members

        /// <summary>
        /// Memory slug (the benchmark document id).
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Score reported by the store.
        /// </summary>
        public double Score { get; set; } = 0.0;

        #endregion
    }
}
