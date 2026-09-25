namespace Test.Benchmark
{
    /// <summary>
    /// Optional search settings sent with a benchmark query. Null members are left out so the server default applies.
    /// </summary>
    public class SearchOptions
    {
        #region Public-Members

        /// <summary>
        /// Hybrid recency weight.
        /// </summary>
        public double? RecencyWeight { get; set; } = null;

        /// <summary>
        /// Minimum score.
        /// </summary>
        public double? MinScore { get; set; } = null;

        /// <summary>
        /// Superseded handling: Demote, Hide, or Include.
        /// </summary>
        public string? Superseded { get; set; } = null;

        /// <summary>
        /// Linked memories to add.
        /// </summary>
        public int? LinkExpansion { get; set; } = null;

        /// <summary>
        /// Result diversity (0 to 1).
        /// </summary>
        public double? Diversity { get; set; } = null;

        /// <summary>
        /// Whether to rerank.
        /// </summary>
        public bool? Rerank { get; set; } = null;

        /// <summary>
        /// Minimum rerank score.
        /// </summary>
        public double? MinRerankScore { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build options from command-line arguments (--recency-weight, --min-score, --superseded, --link-expansion,
        /// --diversity, --min-rerank-score).
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>The options.</returns>
        public static SearchOptions FromArguments(BenchmarkArguments args)
        {
            SearchOptions options = new SearchOptions();
            if (args.GetOptional("recency-weight") != null) options.RecencyWeight = args.GetDouble("recency-weight", 0.1);
            if (args.GetOptional("min-score") != null) options.MinScore = args.GetDouble("min-score", 0.0);
            options.Superseded = args.GetOptional("superseded");
            if (args.GetOptional("link-expansion") != null) options.LinkExpansion = args.GetInt("link-expansion", 0);
            if (args.GetOptional("diversity") != null) options.Diversity = args.GetDouble("diversity", 0.0);
            if (args.GetOptional("min-rerank-score") != null) options.MinRerankScore = args.GetDouble("min-rerank-score", 0.0);
            return options;
        }

        #endregion
    }
}
