namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using Isis.Core.Enums;

    /// <summary>
    /// Task prefixes for embedding model families that are trained with them, looked up by a case-insensitive match
    /// of the model name. Models not listed (for example all-minilm) are embedded without a prefix. Callers can add or
    /// replace entries in <see cref="Prefixes"/>.
    /// </summary>
    public static class EmbeddingPrefixRegistry
    {
        #region Public-Members

        /// <summary>
        /// Prefixes keyed by a model-name fragment. The first key contained in the model name wins, checked in
        /// insertion order.
        /// </summary>
        public static List<KeyValuePair<string, EmbeddingPrefix>> Prefixes { get; } = new List<KeyValuePair<string, EmbeddingPrefix>>
        {
            new KeyValuePair<string, EmbeddingPrefix>("nomic-embed", new EmbeddingPrefix("search_document: ", "search_query: ")),
            new KeyValuePair<string, EmbeddingPrefix>("mxbai-embed", new EmbeddingPrefix(string.Empty, "Represent this sentence for searching relevant passages: ")),
            new KeyValuePair<string, EmbeddingPrefix>("bge-small-en", new EmbeddingPrefix(string.Empty, "Represent this sentence for searching relevant passages: ")),
            new KeyValuePair<string, EmbeddingPrefix>("bge-base-en", new EmbeddingPrefix(string.Empty, "Represent this sentence for searching relevant passages: ")),
            new KeyValuePair<string, EmbeddingPrefix>("bge-large-en", new EmbeddingPrefix(string.Empty, "Represent this sentence for searching relevant passages: ")),
            new KeyValuePair<string, EmbeddingPrefix>("e5-", new EmbeddingPrefix("passage: ", "query: ")),
            new KeyValuePair<string, EmbeddingPrefix>("snowflake-arctic-embed", new EmbeddingPrefix(string.Empty, "Represent this sentence for searching relevant passages: "))
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the prefix a model expects for a purpose.
        /// </summary>
        /// <param name="model">The model name.</param>
        /// <param name="purpose">Document or query.</param>
        /// <returns>The prefix, or an empty string when the model takes none.</returns>
        public static string For(string? model, EmbeddingPurposeEnum purpose)
        {
            if (string.IsNullOrEmpty(model)) return string.Empty;
            foreach (KeyValuePair<string, EmbeddingPrefix> entry in Prefixes)
            {
                if (model.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                return purpose == EmbeddingPurposeEnum.Query ? entry.Value.Query : entry.Value.Document;
            }

            return string.Empty;
        }

        #endregion
    }
}
