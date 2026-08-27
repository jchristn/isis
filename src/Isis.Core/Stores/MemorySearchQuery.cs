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
        /// Optional category filter (category name / label). Null searches all categories in the scope.
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

        #endregion

        #region Private-Members

        private int _TopK = 10;
        private double _TextWeight = 0.5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a memory search query.
        /// </summary>
        public MemorySearchQuery()
        {
        }

        #endregion
    }
}
