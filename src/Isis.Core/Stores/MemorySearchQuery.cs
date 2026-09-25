namespace Isis.Core.Stores
{
    using Isis.Core.Enums;

    /// <summary>
    /// A request to search a scope's memory store.
    /// </summary>
    public class MemorySearchQuery
    {
        #region Public-Members

        /// <summary>
        /// The natural-language or keyword query text.
        /// </summary>
        public string QueryText { get; set; } = string.Empty;

        /// <summary>
        /// The requested retrieval strategy. Providers that cannot honor the request degrade and report it.
        /// </summary>
        public SearchModeEnum Mode { get; set; } = SearchModeEnum.Hybrid;

        /// <summary>
        /// Optional category filter: a category name or cat_ id (the service resolves either to the id, which is
        /// what stores label documents with). Null searches all categories in the scope.
        /// </summary>
        public string? CategoryFilter { get; set; } = null;

        /// <summary>
        /// Maximum number of results to return. Minimum 1, maximum 100, default 10.
        /// </summary>
        public int TopK
        {
            get
            {
                return _TopK;
            }
            set
            {
                if (value < 1) value = 1;
                if (value > 100) value = 100;
                _TopK = value;
            }
        }

        /// <summary>
        /// Optional soft cap on the total characters of snippet text returned, used to bound token usage.
        /// </summary>
        public int? TokenBudget { get; set; } = null;

        /// <summary>
        /// For hybrid search, the weight of the lexical component in the range 0.0 to 1.0. Default 0.5.
        /// The vector component weight is its complement.
        /// </summary>
        public double TextWeight
        {
            get
            {
                return _TextWeight;
            }
            set
            {
                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;
                _TextWeight = value;
            }
        }

        /// <summary>
        /// Optional minimum score. Hits scoring below it are dropped. Null (the default) returns every hit.
        /// The scale depends on the mode: Hybrid scores are fused and normalized to [0, 1]; Semantic scores are vector
        /// similarities; Keyword scores are the store's text relevance.
        /// </summary>
        public double? MinScore { get; set; } = null;

        /// <summary>
        /// For hybrid search, the weight of a recency signal that favors more recently written memories, in the range
        /// 0.0 to 1.0. Default 0.1. 0 disables it. It is fused alongside the vector and text rankings, so a small value
        /// mostly breaks near-ties between similar memories in favor of the newer one (for example a fact and its
        /// replacement); larger values let recency override relevance.
        /// </summary>
        public double RecencyWeight
        {
            get
            {
                return _RecencyWeight;
            }
            set
            {
                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;
                _RecencyWeight = value;
            }
        }

        /// <summary>
        /// How memories replaced by another memory are treated. Default Demote: each replaced memory ranks directly
        /// after its replacement, which is added when the search did not retrieve it.
        /// </summary>
        public SupersededHandlingEnum Superseded { get; set; } = SupersededHandlingEnum.Demote;

        /// <summary>
        /// Maximum number of additional memories to add by following links from the results (a memory's
        /// <c>links</c> and any <c>[[slug]]</c> references in its body). Linked memories are placed directly after
        /// the result that links to them and marked with <c>linkedFrom</c>; they are extra to topK. Minimum 0,
        /// maximum 10, default 0 (off).
        /// </summary>
        public int LinkExpansion
        {
            get
            {
                return _LinkExpansion;
            }
            set
            {
                if (value < 0) value = 0;
                if (value > 10) value = 10;
                _LinkExpansion = value;
            }
        }

        /// <summary>
        /// Result diversity in the range 0.0 to 1.0, applied by maximal marginal relevance: higher values trade
        /// relevance for results that overlap less with the ones ranked above them, so near-duplicate memories do not
        /// crowd out other relevant ones. Default 0 (pure relevance order).
        /// </summary>
        public double Diversity
        {
            get
            {
                return _Diversity;
            }
            set
            {
                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;
                _Diversity = value;
            }
        }

        /// <summary>
        /// Whether to rerank results with the scope's rerank endpoint. Null (the default) reranks when the scope has
        /// a rerank endpoint configured; false skips reranking; true requires a rerank endpoint.
        /// </summary>
        public bool? Rerank { get; set; } = null;

        /// <summary>
        /// Minimum rerank score. Reranked hits scoring below it are dropped. Null uses the scope's
        /// <c>rerankMinScore</c>. Ignored when the search is not reranked.
        /// </summary>
        public double? MinRerankScore { get; set; } = null;

        #endregion

        #region Private-Members

        private int _TopK = 10;
        private double _TextWeight = 0.5;
        private double _RecencyWeight = 0.1;
        private int _LinkExpansion = 0;
        private double _Diversity = 0.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory search query.
        /// </summary>
        public MemorySearchQuery()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a copy of this query.
        /// </summary>
        /// <returns>A new query with the same settings.</returns>
        public MemorySearchQuery Clone()
        {
            return (MemorySearchQuery)MemberwiseClone();
        }

        #endregion
    }
}
