namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Retrieval accuracy benchmark: ingest a labelled dataset, run every query in each search mode, and score the
    /// rankings with Recall@k, MRR@10, nDCG@10 and Hit@1, broken down by query type, alongside latency and a
    /// server-side stage breakdown.
    /// </summary>
    public class RetrievalRunner
    {
        #region Public-Members

        /// <summary>
        /// Metric names reported, in display order.
        /// </summary>
        public static readonly string[] MetricNames = new string[] { "hit@1", "recall@1", "recall@5", "recall@10", "all@5", "all@10", "mrr@10", "ndcg@10" };

        #endregion

        #region Private-Members

        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        public RetrievalRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="dataset">The dataset.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<RetrievalReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            List<string> modes = args.GetList("modes", "Keyword,Semantic,Hybrid");
            int topK = Math.Max(10, args.GetInt("k", 10));
            int concurrency = Math.Max(1, args.GetInt("concurrency", 1));
            bool useCategory = !args.GetFlag("no-category");
            SearchOptions options = SearchOptions.FromArguments(args);
            if (options.Decompose) options.InferenceEndpointId = await _Context.PrepareInferenceAsync(token).ConfigureAwait(false);

            RetrievalReport report = new RetrievalReport
            {
                Dataset = dataset.Name,
                Description = dataset.Description,
                Environment = _Context.Environment
            };
            report.Config["modes"] = string.Join(",", modes);
            report.Config["topK"] = topK.ToString();
            report.Config["concurrency"] = concurrency.ToString();
            report.Config["categoryFilter"] = useCategory ? "on" : "off";
            report.Config["store"] = args.Get("store", "RecallDb");
            report.Config["recencyWeight"] = options.RecencyWeight.HasValue ? options.RecencyWeight.Value.ToString("0.###") : "server default";
            if (options.MinScore.HasValue) report.Config["minScore"] = options.MinScore.Value.ToString("0.###");
            report.Config["rerank"] = args.GetFlag("rerank") ? "on" : "off";
            foreach (string name in new string[] { "chunking-mode", "chunk-strategy", "chunk-max-tokens", "chunk-overlap", "scope-suffix", "superseded", "link-expansion", "diversity", "min-rerank-score", "rerank-candidates", "text-weight", "rrf-k", "decompose" })
            {
                string? value = args.GetOptional(name);
                if (value != null) report.Config[name] = value;
            }

            Console.WriteLine("[retrieval] " + dataset.Name + ": " + dataset.Corpora.Count + " corpora, "
                + dataset.Corpora.Sum(c => c.Documents.Count) + " documents, " + dataset.Corpora.Sum(c => c.Queries.Count) + " queries");

            ScopeProvisioner provisioner = new ScopeProvisioner(_Context);
            List<ProvisionedScope> scopes = await provisioner.ProvisionAsync(dataset, report.Ingest, token).ConfigureAwait(false);
            Console.WriteLine("[retrieval] ingest: " + report.Ingest.Documents + " docs in " + report.Ingest.WallSeconds + "s ("
                + report.Ingest.DocumentsPerSecond + " docs/s), " + report.Ingest.ReusedScopes + " scopes reused, " + report.Ingest.Failures + " failures");

            foreach (string mode in modes)
            {
                PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);
                List<QueryOutcome> outcomes = await RunModeAsync(scopes, mode, topK, concurrency, useCategory, options, token).ConfigureAwait(false);
                PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);

                ModeSummary summary = Summarize(mode, outcomes);
                summary.Stages = after.Since(before);
                report.Modes.Add(summary);
                report.Outcomes.AddRange(outcomes);
                Console.WriteLine("[retrieval] " + mode.PadRight(8) + " recall@5 " + Format(summary.Metrics, "recall@5") + "  mrr@10 " + Format(summary.Metrics, "mrr@10")
                    + "  ndcg@10 " + Format(summary.Metrics, "ndcg@10") + "  p50 " + summary.Latency.P50 + "ms  p95 " + summary.Latency.P95 + "ms  errors " + summary.Errors);
            }

            if (args.GetFlag("cleanup")) await provisioner.CleanupAsync(scopes, token).ConfigureAwait(false);
            return report;
        }

        #endregion

        #region Private-Methods

        private async Task<List<QueryOutcome>> RunModeAsync(List<ProvisionedScope> scopes, string mode, int topK, int concurrency, bool useCategory, SearchOptions options, CancellationToken token)
        {
            ConcurrentBag<QueryOutcome> outcomes = new ConcurrentBag<QueryOutcome>();
            using SemaphoreSlim gate = new SemaphoreSlim(concurrency);
            List<Task> tasks = new List<Task>();

            foreach (ProvisionedScope scope in scopes)
            {
                foreach (BenchmarkQuery query in scope.Corpus.Queries)
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string? category = useCategory ? query.Category : null;
                            SearchResponse response = await _Context.Client.SearchAsync(scope.ScopeId, query.Text, mode, topK, category, token, options).ConfigureAwait(false);
                            outcomes.Add(Score(scope.Corpus.Id, query, mode, response));
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, token));
                }
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return outcomes.OrderBy(o => o.Corpus, StringComparer.Ordinal).ThenBy(o => o.QueryId, StringComparer.Ordinal).ToList();
        }

        private static QueryOutcome Score(string corpus, BenchmarkQuery query, string mode, SearchResponse response)
        {
            QueryOutcome outcome = new QueryOutcome
            {
                Corpus = corpus,
                QueryId = query.Id,
                Type = query.Type,
                Mode = mode,
                EffectiveMode = response.EffectiveMode,
                StatusCode = response.StatusCode,
                LatencyMs = Math.Round(response.ElapsedMs, 2),
                Ranked = response.Hits.Select(h => h.Slug).ToList(),
                Relevant = new List<string>(query.Relevant),
                TopScore = response.Hits.Count > 0 ? response.Hits[0].Score : 0.0,
                TopVectorScore = response.Hits.Count > 0 ? response.Hits.Max(h => h.VectorScore ?? 0.0) : 0.0
            };

            if (!query.Answerable || !response.IsSuccess) return outcome;

            HashSet<string> relevant = new HashSet<string>(query.Relevant, StringComparer.Ordinal);
            Dictionary<string, int> grades = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in query.Relevant) grades[id] = 1;
            if (query.Grades != null)
            {
                foreach (KeyValuePair<string, int> grade in query.Grades) grades[grade.Key] = grade.Value;
            }

            outcome.Metrics["hit@1"] = outcome.Ranked.Count > 0 && relevant.Contains(outcome.Ranked[0]) ? 1.0 : 0.0;
            outcome.Metrics["recall@1"] = RetrievalMetrics.RecallAtK(outcome.Ranked, relevant, 1);
            outcome.Metrics["recall@5"] = RetrievalMetrics.RecallAtK(outcome.Ranked, relevant, 5);
            outcome.Metrics["recall@10"] = RetrievalMetrics.RecallAtK(outcome.Ranked, relevant, 10);
            outcome.Metrics["all@5"] = RetrievalMetrics.AllAtK(outcome.Ranked, relevant, 5);
            outcome.Metrics["all@10"] = RetrievalMetrics.AllAtK(outcome.Ranked, relevant, 10);
            outcome.Metrics["mrr@10"] = RetrievalMetrics.ReciprocalRank(outcome.Ranked, relevant, 10);
            outcome.Metrics["ndcg@10"] = RetrievalMetrics.NdcgAtK(outcome.Ranked, grades, 10);
            return outcome;
        }

        private static ModeSummary Summarize(string mode, List<QueryOutcome> outcomes)
        {
            List<QueryOutcome> scored = outcomes.Where(o => o.Metrics.Count > 0).ToList();
            List<QueryOutcome> negatives = outcomes.Where(o => o.Relevant.Count == 0 && o.StatusCode >= 200 && o.StatusCode < 300).ToList();

            ModeSummary summary = new ModeSummary
            {
                Mode = mode,
                Queries = scored.Count,
                NegativeQueries = negatives.Count,
                Errors = outcomes.Count(o => o.StatusCode < 200 || o.StatusCode >= 300),
                ModeMismatches = outcomes.Count(o => !string.IsNullOrEmpty(o.EffectiveMode) && !string.Equals(o.EffectiveMode, mode, StringComparison.OrdinalIgnoreCase)),
                Metrics = Mean(scored),
                MeanTopScoreAnswerable = scored.Count > 0 ? Math.Round(scored.Average(o => o.TopScore), 4) : 0.0,
                MeanTopScoreNegative = negatives.Count > 0 ? Math.Round(negatives.Average(o => o.TopScore), 4) : 0.0,
                ScoreAuroc = Auroc(scored.Select(o => o.TopScore).ToList(), negatives.Select(o => o.TopScore).ToList()),
                AnswerableEmptyRate = scored.Count > 0 ? Math.Round(scored.Count(o => o.Ranked.Count == 0) / (double)scored.Count, 4) : 0.0,
                NegativeEmptyRate = negatives.Count > 0 ? Math.Round(negatives.Count(o => o.Ranked.Count == 0) / (double)negatives.Count, 4) : 0.0,
                VectorScoreAuroc = Auroc(scored.Select(o => o.TopVectorScore).ToList(), negatives.Select(o => o.TopVectorScore).ToList()),
                Latency = LatencyStats.From(outcomes.Select(o => o.LatencyMs))
            };

            foreach (IGrouping<string, QueryOutcome> group in scored.GroupBy(o => o.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Dictionary<string, double> metrics = Mean(group.ToList());
                metrics["count"] = group.Count();
                summary.ByType[group.Key] = metrics;
            }

            return summary;
        }

        private static double? Auroc(List<double> positives, List<double> negatives)
        {
            // Mann-Whitney form: fraction of (positive, negative) pairs ranked correctly, ties counting half.
            if (positives.Count == 0 || negatives.Count == 0) return null;
            if (positives.All(p => p == 0.0) && negatives.All(n => n == 0.0)) return null;
            double wins = 0.0;
            foreach (double p in positives)
            {
                foreach (double n in negatives)
                {
                    if (p > n) wins += 1.0;
                    else if (p == n) wins += 0.5;
                }
            }

            return Math.Round(wins / (positives.Count * (double)negatives.Count), 4);
        }

        private static Dictionary<string, double> Mean(List<QueryOutcome> outcomes)
        {
            Dictionary<string, double> means = new Dictionary<string, double>();
            if (outcomes.Count == 0) return means;
            foreach (string name in MetricNames)
            {
                means[name] = Math.Round(outcomes.Average(o => o.Metrics.TryGetValue(name, out double v) ? v : 0.0), 4);
            }

            return means;
        }

        private static string Format(Dictionary<string, double> metrics, string name)
        {
            return metrics.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion
    }
}
