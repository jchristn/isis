namespace Isis.Core.Stores.RecallDb
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using global::RecallDb.Sdk.Models;

    /// <summary>
    /// Weighted reciprocal-rank fusion of a vector ranking and a full-text ranking, with an optional recency signal.
    /// Pure and deterministic, so it can be tested without a store. Thread-safe (no shared state).
    /// </summary>
    /// <remarks>
    /// For each candidate chunk: raw = (1 − w) / (k + vectorRank) + w / (k + textRank) + r / (k + recencyRank), where a
    /// missing rank contributes 0, w is the text weight and r the recency weight. The score is normalized by the best
    /// possible raw score, ((1 − w) + w + r) / (k + 1), so it lies in [0, 1]. Recency ranks the candidates' parent
    /// memories by write time (newest first); every chunk of a memory shares its memory's recency rank.
    /// </remarks>
    public static class HybridFusion
    {
        #region Public-Members

        /// <summary>
        /// The conventional RRF constant.
        /// </summary>
        public const int DefaultRrfK = 60;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fuse two rankings.
        /// </summary>
        /// <param name="vectorResults">Vector-leg documents, best first. May be null.</param>
        /// <param name="textResults">Text-leg documents, best first. May be null.</param>
        /// <param name="textWeight">Text-leg weight in [0, 1]; the vector leg gets 1 − textWeight.</param>
        /// <param name="recencyWeight">Recency weight in [0, 1]; 0 disables the recency signal.</param>
        /// <param name="parentKey">Maps a chunk document to its parent memory key (used for recency).</param>
        /// <param name="rrfK">RRF constant, at least 1. Default 60.</param>
        /// <returns>Fused documents, best first. Ties are broken by document key for determinism.</returns>
        /// <exception cref="ArgumentNullException">Thrown when parentKey is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a weight is outside [0, 1] or rrfK is below 1.</exception>
        public static List<FusedDocument> Fuse(
            IReadOnlyList<DocumentRecord>? vectorResults,
            IReadOnlyList<DocumentRecord>? textResults,
            double textWeight,
            double recencyWeight,
            Func<DocumentRecord, string> parentKey,
            int rrfK = DefaultRrfK)
        {
            ArgumentNullException.ThrowIfNull(parentKey);
            if (textWeight < 0.0 || textWeight > 1.0) throw new ArgumentOutOfRangeException(nameof(textWeight), "Text weight must be in [0, 1].");
            if (recencyWeight < 0.0 || recencyWeight > 1.0) throw new ArgumentOutOfRangeException(nameof(recencyWeight), "Recency weight must be in [0, 1].");
            if (rrfK < 1) throw new ArgumentOutOfRangeException(nameof(rrfK), "The RRF constant must be at least 1.");

            Dictionary<string, FusedDocument> byKey = new Dictionary<string, FusedDocument>(StringComparer.Ordinal);
            AddLeg(byKey, vectorResults, true);
            AddLeg(byKey, textResults, false);

            if (recencyWeight > 0.0) AssignRecencyRanks(byKey.Values, parentKey);

            double vectorWeight = 1.0 - textWeight;
            double best = (vectorWeight + textWeight + (recencyWeight > 0.0 ? recencyWeight : 0.0)) / (rrfK + 1);
            foreach (FusedDocument fused in byKey.Values)
            {
                double raw = 0.0;
                if (fused.VectorRank.HasValue) raw += vectorWeight / (rrfK + fused.VectorRank.Value);
                if (fused.TextRank.HasValue) raw += textWeight / (rrfK + fused.TextRank.Value);
                if (fused.RecencyRank.HasValue) raw += recencyWeight / (rrfK + fused.RecencyRank.Value);
                fused.Score = best > 0.0 ? raw / best : 0.0;
            }

            return byKey.Values
                .OrderByDescending(f => f.Score)
                .ThenBy(f => f.Document.DocumentKey ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        #endregion

        #region Private-Methods

        private static void AddLeg(Dictionary<string, FusedDocument> byKey, IReadOnlyList<DocumentRecord>? leg, bool isVector)
        {
            if (leg == null) return;
            for (int i = 0; i < leg.Count; i++)
            {
                DocumentRecord document = leg[i];
                if (document == null) continue;
                string key = document.DocumentKey ?? document.DocumentId ?? ("#" + document.Id);
                if (!byKey.TryGetValue(key, out FusedDocument? fused))
                {
                    fused = new FusedDocument(document);
                    byKey[key] = fused;
                }

                if (isVector && !fused.VectorRank.HasValue)
                {
                    fused.VectorRank = i + 1;
                    fused.VectorScore = document.Score;
                }
                else if (!isVector && !fused.TextRank.HasValue)
                {
                    fused.TextRank = i + 1;
                    fused.TextScore = document.Score;
                }
            }
        }

        private static void AssignRecencyRanks(IEnumerable<FusedDocument> candidates, Func<DocumentRecord, string> parentKey)
        {
            // One rank per parent memory: every chunk of a memory is written together, so the newest chunk time is the
            // memory's write time.
            Dictionary<string, DateTime> newest = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            List<FusedDocument> all = candidates.ToList();
            foreach (FusedDocument fused in all)
            {
                string parent = parentKey(fused.Document);
                DateTime created = fused.Document.CreatedUtc;
                if (!newest.TryGetValue(parent, out DateTime existing) || created > existing) newest[parent] = created;
            }

            Dictionary<string, int> rank = new Dictionary<string, int>(StringComparer.Ordinal);
            int next = 1;
            foreach (KeyValuePair<string, DateTime> entry in newest.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                rank[entry.Key] = next++;
            }

            foreach (FusedDocument fused in all)
            {
                fused.RecencyRank = rank[parentKey(fused.Document)];
            }
        }

        #endregion
    }
}
