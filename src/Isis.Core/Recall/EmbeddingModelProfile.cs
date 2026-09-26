namespace Isis.Core.Recall
{
    /// <summary>
    /// What Isis knows about an embedding model family, matched by a fragment of the model name. Every setting is
    /// optional: a null value means the generic default applies. Keep entries to settings a benchmark has shown to
    /// matter, so the table stays small as models change.
    /// </summary>
    public class EmbeddingModelProfile
    {
        #region Public-Members

        /// <summary>
        /// Case-insensitive fragment of the model name this profile applies to, for example "nomic-embed".
        /// </summary>
        public string Match { get; set; } = string.Empty;

        /// <summary>
        /// Task prefix the model expects in front of stored content. Empty for none.
        /// </summary>
        public string DocumentPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Task prefix the model expects in front of search queries. Empty for none.
        /// </summary>
        public string QueryPrefix { get; set; } = string.Empty;

        /// <summary>
        /// Hybrid text-leg weight (0 to 1) for this model, or null for the generic default.
        /// </summary>
        public double? TextWeight { get; set; } = null;

        /// <summary>
        /// Reciprocal-rank-fusion constant for this model, or null for the generic default.
        /// </summary>
        public int? RrfK { get; set; } = null;

        /// <summary>
        /// Default chunk size as a fraction of the model's token budget, or null for the generic default.
        /// </summary>
        public double? ChunkFraction { get; set; } = null;

        /// <summary>
        /// Largest default chunk in tokens, or null for the generic default.
        /// </summary>
        public int? ChunkMaxTokens { get; set; } = null;

        #endregion
    }
}
