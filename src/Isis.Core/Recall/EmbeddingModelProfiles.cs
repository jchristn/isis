namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using Isis.Core.Enums;

    /// <summary>
    /// The registry of embedding model profiles. Models not listed use the generic defaults (no task prefix, the
    /// default hybrid weights, and the default chunk size), so an unknown model always works; entries only add what a
    /// benchmark has shown a model needs. Callers can add or replace entries in <see cref="Known"/>.
    /// </summary>
    public static class EmbeddingModelProfiles
    {
        #region Public-Members

        /// <summary>
        /// Known model families. The first entry whose <see cref="EmbeddingModelProfile.Match"/> is contained in the
        /// model name wins, checked in list order.
        /// </summary>
        public static List<EmbeddingModelProfile> Known { get; } = new List<EmbeddingModelProfile>
        {
            new EmbeddingModelProfile { Match = "nomic-embed", DocumentPrefix = "search_document: ", QueryPrefix = "search_query: ", ChunkMaxTokens = 128 },
            new EmbeddingModelProfile { Match = "mxbai-embed", QueryPrefix = "Represent this sentence for searching relevant passages: " },
            new EmbeddingModelProfile { Match = "bge-small-en", QueryPrefix = "Represent this sentence for searching relevant passages: " },
            new EmbeddingModelProfile { Match = "bge-base-en", QueryPrefix = "Represent this sentence for searching relevant passages: " },
            new EmbeddingModelProfile { Match = "bge-large-en", QueryPrefix = "Represent this sentence for searching relevant passages: " },
            new EmbeddingModelProfile { Match = "snowflake-arctic-embed", QueryPrefix = "Represent this sentence for searching relevant passages: " },
            new EmbeddingModelProfile { Match = "e5-", DocumentPrefix = "passage: ", QueryPrefix = "query: " }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find the profile for a model.
        /// </summary>
        /// <param name="model">The model name.</param>
        /// <returns>The matching profile, or null when the model is not listed.</returns>
        public static EmbeddingModelProfile? Find(string? model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            foreach (EmbeddingModelProfile profile in Known)
            {
                if (!string.IsNullOrEmpty(profile.Match) && model.IndexOf(profile.Match, StringComparison.OrdinalIgnoreCase) >= 0) return profile;
            }

            return null;
        }

        /// <summary>
        /// The task prefix a model expects for a purpose.
        /// </summary>
        /// <param name="model">The model name.</param>
        /// <param name="purpose">Document or query.</param>
        /// <returns>The prefix, or an empty string.</returns>
        public static string Prefix(string? model, EmbeddingPurposeEnum purpose)
        {
            EmbeddingModelProfile? profile = Find(model);
            if (profile == null) return string.Empty;
            return purpose == EmbeddingPurposeEnum.Query ? profile.QueryPrefix : profile.DocumentPrefix;
        }

        #endregion
    }
}
