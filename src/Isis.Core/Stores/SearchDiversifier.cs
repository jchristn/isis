namespace Isis.Core.Stores
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Reorders search hits by maximal marginal relevance (MMR), so that results which largely repeat a
    /// higher-ranked result give way to other relevant results. Similarity between hits is the overlap (Jaccard) of
    /// their word sets, which works for every store and needs no stored vectors.
    /// </summary>
    public static class SearchDiversifier
    {
        #region Public-Methods

        /// <summary>
        /// Reorder hits by maximal marginal relevance.
        /// </summary>
        /// <param name="hits">Hits ordered best-first.</param>
        /// <param name="diversity">Diversity in the range 0.0 to 1.0. 0 returns the input order; 1 ranks purely by
        /// dissimilarity after the first hit.</param>
        /// <param name="count">Maximum number of hits to return.</param>
        /// <returns>The reordered hits, at most <paramref name="count"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when hits is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when diversity is outside [0, 1] or count is negative.</exception>
        public static List<MemorySearchHit> Diversify(IReadOnlyList<MemorySearchHit> hits, double diversity, int count)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            if (diversity < 0.0 || diversity > 1.0) throw new ArgumentOutOfRangeException(nameof(diversity), "Diversity must be in [0, 1].");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count may not be negative.");

            if (diversity <= 0.0 || hits.Count <= 1) return hits.Take(count).ToList();

            double maxScore = hits.Max(h => h.Score);
            double minScore = hits.Min(h => h.Score);
            double range = maxScore - minScore;
            double lambda = 1.0 - diversity;

            List<HashSet<string>> terms = hits.Select(h => Terms((h.Title ?? string.Empty) + " " + h.Snippet)).ToList();
            List<int> remaining = Enumerable.Range(0, hits.Count).ToList();
            List<int> selected = new List<int>();
            double[] maxSimilarity = new double[hits.Count];

            while (remaining.Count > 0 && selected.Count < count)
            {
                int best = -1;
                double bestValue = double.NegativeInfinity;
                foreach (int candidate in remaining)
                {
                    // Relevance is rescaled to [0, 1] within the candidate set so it is comparable with similarity.
                    double relevance = range > 0.0 ? (hits[candidate].Score - minScore) / range : 1.0;
                    double value = lambda * relevance - (1.0 - lambda) * maxSimilarity[candidate];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = candidate;
                    }
                }

                selected.Add(best);
                remaining.Remove(best);
                foreach (int candidate in remaining)
                {
                    double similarity = Jaccard(terms[best], terms[candidate]);
                    if (similarity > maxSimilarity[candidate]) maxSimilarity[candidate] = similarity;
                }
            }

            return selected.Select(i => hits[i]).ToList();
        }

        /// <summary>
        /// Word-set overlap of two texts (Jaccard similarity of their lowercase words of two or more characters).
        /// </summary>
        /// <param name="a">First text.</param>
        /// <param name="b">Second text.</param>
        /// <returns>Similarity in the range 0.0 to 1.0.</returns>
        public static double Similarity(string? a, string? b)
        {
            return Jaccard(Terms(a ?? string.Empty), Terms(b ?? string.Empty));
        }

        #endregion

        #region Private-Methods

        private static HashSet<string> Terms(string text)
        {
            HashSet<string> terms = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder current = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                    continue;
                }

                if (current.Length >= 2) terms.Add(current.ToString());
                current.Clear();
            }

            if (current.Length >= 2) terms.Add(current.ToString());
            return terms;
        }

        private static double Jaccard(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0.0;
            int shared = 0;
            foreach (string term in a)
            {
                if (b.Contains(term)) shared++;
            }

            int union = a.Count + b.Count - shared;
            return union > 0 ? (double)shared / union : 0.0;
        }

        #endregion
    }
}
