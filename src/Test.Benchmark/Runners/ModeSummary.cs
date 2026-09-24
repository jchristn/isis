namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Aggregate retrieval results for one search mode.
    /// </summary>
    public class ModeSummary
    {
        #region Public-Members

        /// <summary>
        /// Search mode.
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// Answerable queries scored.
        /// </summary>
        public int Queries { get; set; } = 0;

        /// <summary>
        /// Unanswerable (negative) queries run.
        /// </summary>
        public int NegativeQueries { get; set; } = 0;

        /// <summary>
        /// Failed requests.
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Queries where the server used a different mode than requested.
        /// </summary>
        public int ModeMismatches { get; set; } = 0;

        /// <summary>
        /// Mean metric values over answerable queries.
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Mean metric values per query type.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByType { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Mean top-hit score for answerable queries.
        /// </summary>
        public double MeanTopScoreAnswerable { get; set; } = 0.0;

        /// <summary>
        /// Mean top-hit score for unanswerable queries. Close to the answerable mean means scores cannot be used
        /// as a relevance threshold to say "nothing relevant".
        /// </summary>
        public double MeanTopScoreNegative { get; set; } = 0.0;

        /// <summary>
        /// AUROC of the top-hit score as a classifier of answerable vs unanswerable questions: the probability that
        /// a random answerable question's top score exceeds a random unanswerable one's. 0.5 is no signal, 1.0 is a
        /// perfect threshold. Null without both kinds of question.
        /// </summary>
        public double? ScoreAuroc { get; set; } = null;

        /// <summary>
        /// The same AUROC computed on the top raw vector similarity instead of the reported score.
        /// </summary>
        public double? VectorScoreAuroc { get; set; } = null;

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stage breakdown for this mode's queries.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        #endregion
    }
}
