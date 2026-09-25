namespace Test.Benchmark
{
    using System.Collections.Generic;

    /// <summary>
    /// A timed, parsed search response.
    /// </summary>
    public class SearchResponse : TimedResponse
    {
        #region Public-Members

        /// <summary>
        /// The mode the server actually used.
        /// </summary>
        public string EffectiveMode { get; set; } = string.Empty;

        /// <summary>
        /// Hits, best first.
        /// </summary>
        public List<SearchHit> Hits { get; set; } = new List<SearchHit>();

        /// <summary>
        /// Whether the server reranked the hits.
        /// </summary>
        public bool Reranked { get; set; } = false;

        #endregion
    }
}
