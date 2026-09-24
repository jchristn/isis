namespace Test.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Standard ranked-retrieval metrics over a ranked list of document ids and a set of relevance labels.
    /// nDCG uses linear gain (gain = grade), matching pytrec_eval / BEIR, so results are comparable to published numbers.
    /// </summary>
    public static class RetrievalMetrics
    {
        #region Public-Methods

        /// <summary>
        /// Fraction of the relevant documents that appear in the top k.
        /// </summary>
        /// <param name="ranked">Ranked document ids, best first.</param>
        /// <param name="relevant">Relevant document ids.</param>
        /// <param name="k">Cutoff.</param>
        /// <returns>Recall at k in [0, 1]; 0 when there are no relevant documents.</returns>
        public static double RecallAtK(IReadOnlyList<string> ranked, IReadOnlyCollection<string> relevant, int k)
        {
            if (relevant == null || relevant.Count == 0) return 0.0;
            int found = ranked.Take(k).Distinct(StringComparer.Ordinal).Count(id => relevant.Contains(id));
            return (double)found / relevant.Count;
        }

        /// <summary>
        /// 1 when every relevant document is in the top k (a stricter recall for multi-document questions).
        /// </summary>
        /// <param name="ranked">Ranked document ids, best first.</param>
        /// <param name="relevant">Relevant document ids.</param>
        /// <param name="k">Cutoff.</param>
        /// <returns>1 or 0.</returns>
        public static double AllAtK(IReadOnlyList<string> ranked, IReadOnlyCollection<string> relevant, int k)
        {
            if (relevant == null || relevant.Count == 0) return 0.0;
            HashSet<string> top = new HashSet<string>(ranked.Take(k), StringComparer.Ordinal);
            return relevant.All(id => top.Contains(id)) ? 1.0 : 0.0;
        }

        /// <summary>
        /// Reciprocal rank of the first relevant document within the top k.
        /// </summary>
        /// <param name="ranked">Ranked document ids, best first.</param>
        /// <param name="relevant">Relevant document ids.</param>
        /// <param name="k">Cutoff.</param>
        /// <returns>1/rank, or 0 when no relevant document is in the top k.</returns>
        public static double ReciprocalRank(IReadOnlyList<string> ranked, IReadOnlyCollection<string> relevant, int k)
        {
            if (relevant == null || relevant.Count == 0) return 0.0;
            int limit = Math.Min(k, ranked.Count);
            for (int i = 0; i < limit; i++)
            {
                if (relevant.Contains(ranked[i])) return 1.0 / (i + 1);
            }

            return 0.0;
        }

        /// <summary>
        /// Normalized discounted cumulative gain at k.
        /// </summary>
        /// <param name="ranked">Ranked document ids, best first.</param>
        /// <param name="grades">Document id to gain (documents absent have gain 0).</param>
        /// <param name="k">Cutoff.</param>
        /// <returns>nDCG at k in [0, 1].</returns>
        public static double NdcgAtK(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> grades, int k)
        {
            if (grades == null || grades.Count == 0) return 0.0;

            double dcg = 0.0;
            HashSet<string> counted = new HashSet<string>(StringComparer.Ordinal);
            int limit = Math.Min(k, ranked.Count);
            for (int i = 0; i < limit; i++)
            {
                if (!counted.Add(ranked[i])) continue;
                if (grades.TryGetValue(ranked[i], out int gain) && gain > 0) dcg += gain / Math.Log2(i + 2);
            }

            double idcg = 0.0;
            List<int> ideal = grades.Values.Where(g => g > 0).OrderByDescending(g => g).Take(k).ToList();
            for (int i = 0; i < ideal.Count; i++)
            {
                idcg += ideal[i] / Math.Log2(i + 2);
            }

            return idcg > 0.0 ? dcg / idcg : 0.0;
        }

        #endregion
    }
}
