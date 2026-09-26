namespace Isis.Server.Settings
{
    using System;

    /// <summary>
    /// Server-wide retrieval settings: the similarity check on upsert, reranker input size, and chat link expansion.
    /// Per-scope rerank settings live on the scope.
    /// </summary>
    public class RetrievalSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether an upsert reports existing memories that closely resemble the one written (in
        /// <c>similarMemories</c>). Only scopes with semantic search are checked. Default true.
        /// </summary>
        public bool DuplicateCheckEnabled { get; set; } = true;

        /// <summary>
        /// Minimum vector similarity for an existing memory to be reported as similar, in the range 0.0 to 1.0.
        /// Default 0.85 (with all-minilm, replaced facts score about 0.55 to 0.88 against their replacement).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0, 1].</exception>
        public double DuplicateSimilarityThreshold
        {
            get
            {
                return _DuplicateSimilarityThreshold;
            }
            set
            {
                if (value < 0.0 || value > 1.0) throw new ArgumentOutOfRangeException(nameof(DuplicateSimilarityThreshold), "DuplicateSimilarityThreshold must be in [0, 1].");
                _DuplicateSimilarityThreshold = value;
            }
        }

        /// <summary>
        /// Characters of each candidate sent to a reranker. Minimum 100, default 1200.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 100.</exception>
        public int RerankPassageChars
        {
            get
            {
                return _RerankPassageChars;
            }
            set
            {
                if (value < 100) throw new ArgumentOutOfRangeException(nameof(RerankPassageChars), "RerankPassageChars must be at least 100.");
                _RerankPassageChars = value;
            }
        }

        /// <summary>
        /// Whether chat asks its inference endpoint to split a multi-part question into sub-queries before retrieval.
        /// Default false.
        /// </summary>
        public bool ChatQueryDecomposition { get; set; } = false;

        /// <summary>
        /// How many chunks of one memory are embedded at the same time. Minimum 1, maximum 32, default 4. Lower it for an
        /// embedding endpoint that accepts only a few concurrent requests.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [1, 32].</exception>
        public int EmbeddingParallelism
        {
            get
            {
                return _EmbeddingParallelism;
            }
            set
            {
                if (value < 1 || value > 32) throw new ArgumentOutOfRangeException(nameof(EmbeddingParallelism), "EmbeddingParallelism must be between 1 and 32.");
                _EmbeddingParallelism = value;
            }
        }

        /// <summary>
        /// How many linked memories chat adds to its grounding context by following links from the retrieved ones.
        /// Minimum 0, maximum 10, default 2.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0, 10].</exception>
        public int ChatLinkExpansion
        {
            get
            {
                return _ChatLinkExpansion;
            }
            set
            {
                if (value < 0 || value > 10) throw new ArgumentOutOfRangeException(nameof(ChatLinkExpansion), "ChatLinkExpansion must be between 0 and 10.");
                _ChatLinkExpansion = value;
            }
        }

        #endregion

        #region Private-Members

        private double _DuplicateSimilarityThreshold = 0.85;
        private int _RerankPassageChars = 1200;
        private int _ChatLinkExpansion = 2;
        private int _EmbeddingParallelism = 4;

        #endregion
    }
}
