namespace Test.Benchmark
{
    using System.Collections.Generic;

    /// <summary>
    /// A timed memory upsert, with the similar memories the server reported.
    /// </summary>
    public class UpsertResponse : TimedResponse
    {
        #region Public-Members

        /// <summary>
        /// Slugs of the existing memories the server reported as similar (similarMemories).
        /// </summary>
        public List<string> SimilarSlugs { get; set; } = new List<string>();

        #endregion
    }
}
