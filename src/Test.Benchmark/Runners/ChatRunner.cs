namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// End-to-end chat-with-memory (RAG) benchmark: ask each labelled question through the Isis chat route, then
    /// score answer correctness with an independent LLM judge, abstention on unanswerable questions, citation
    /// precision/recall (parsed from the answer text), and whether retrieval put the evidence in the prompt at all.
    /// </summary>
    public class ChatRunner
    {
        #region Private-Members

        private static readonly Regex _Citation = new Regex("\\[([^\\[\\]]{1,200})\\]", RegexOptions.Compiled);
        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        public ChatRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="dataset">Dataset with gold answers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<ChatReport> RunAsync(BenchmarkDataset dataset, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            int topK = args.GetInt("k", 0);
            int limit = args.GetInt("limit", 0);
            int concurrency = Math.Max(1, args.GetInt("concurrency", 1));
            string inferenceId = await _Context.PrepareInferenceAsync(token).ConfigureAwait(false);
            JudgeClient? judge = CreateJudge(args);

            ChatReport report = new ChatReport { Dataset = dataset.Name, Environment = _Context.Environment };
            report.Config["topK"] = topK > 0 ? topK.ToString() : "server default";
            report.Config["judge"] = judge != null ? judge.Description : "none";
            report.Config["concurrency"] = concurrency.ToString();
            if (limit > 0) report.Config["limit"] = limit.ToString();

            IngestSummary ingest = new IngestSummary();
            List<ProvisionedScope> scopes = await new ScopeProvisioner(_Context).ProvisionAsync(dataset, ingest, token).ConfigureAwait(false);
            Console.WriteLine("[chat] ingest: " + ingest.Documents + " docs, " + ingest.ReusedScopes + " scopes reused, " + ingest.Failures + " failures");

            List<KeyValuePair<ProvisionedScope, BenchmarkQuery>> work = new List<KeyValuePair<ProvisionedScope, BenchmarkQuery>>();
            foreach (ProvisionedScope scope in scopes)
            {
                foreach (BenchmarkQuery query in scope.Corpus.Queries)
                {
                    if (string.IsNullOrEmpty(query.Answer)) continue;
                    work.Add(new KeyValuePair<ProvisionedScope, BenchmarkQuery>(scope, query));
                }
            }

            if (limit > 0 && work.Count > limit) work = Spread(work, limit);

            ConcurrentBag<ChatItem> items = new ConcurrentBag<ChatItem>();
            PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);
            using SemaphoreSlim gate = new SemaphoreSlim(concurrency);
            List<Task> tasks = new List<Task>();
            int done = 0;
            foreach (KeyValuePair<ProvisionedScope, BenchmarkQuery> pair in work)
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        items.Add(await AskAsync(pair.Key, pair.Value, topK, inferenceId, judge, token).ConfigureAwait(false));
                        int n = Interlocked.Increment(ref done);
                        if (n % 10 == 0) Console.WriteLine("  answered " + n + "/" + work.Count);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);

            report.Items = items.OrderBy(i => i.Corpus, StringComparer.Ordinal).ThenBy(i => i.QueryId, StringComparer.Ordinal).ToList();
            report.Stages = after.Since(before);
            report.Latency = LatencyStats.From(report.Items.Where(i => i.Error == null).Select(i => i.LatencyMs));
            report.Summary = Summarize(report.Items);
            foreach (IGrouping<string, ChatItem> group in report.Items.GroupBy(i => i.Type).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                report.ByType[group.Key] = Summarize(group.ToList());
            }

            Console.WriteLine("[chat] accuracy " + Format(report.Summary, "accuracy") + "  abstention " + Format(report.Summary, "abstentionAccuracy")
                + "  context recall " + Format(report.Summary, "contextRecall") + "  citation P/R " + Format(report.Summary, "citationPrecision") + "/" + Format(report.Summary, "citationRecall")
                + "  p50 " + report.Latency.P50 + "ms");
            return report;
        }

        #endregion

        #region Private-Methods

        private JudgeClient? CreateJudge(BenchmarkArguments args)
        {
            string format = args.Get("judge-format", "Ollama");
            if (string.Equals(format, "none", StringComparison.OrdinalIgnoreCase)) return null;
            return new JudgeClient(
                _Context.Http,
                format,
                args.Get("judge-url", "http://127.0.0.1:11434"),
                args.Get("judge-model", args.Get("inference-model", "gemma3:4b")),
                args.GetOptional("judge-api-key"));
        }

        private async Task<ChatItem> AskAsync(ProvisionedScope scope, BenchmarkQuery query, int topK, string inferenceId, JudgeClient? judge, CancellationToken token)
        {
            string question = string.IsNullOrEmpty(query.Date) ? query.Text : "(Current date: " + query.Date + ") " + query.Text;
            ChatResponse response = await _Context.Client.ChatAsync(scope.ScopeId, question, topK, inferenceId, token).ConfigureAwait(false);

            ChatItem item = new ChatItem
            {
                Corpus = scope.Corpus.Id,
                QueryId = query.Id,
                Type = query.Type,
                Question = question,
                Gold = query.Answer ?? string.Empty,
                StatusCode = response.StatusCode,
                LatencyMs = Math.Round(response.ElapsedMs, 1),
                Relevant = new List<string>(query.Relevant),
                Retrieved = response.RetrievedSlugs,
                Error = response.Error
            };

            if (!response.IsSuccess) return item;

            item.Answer = JudgeClient.StripThinking(response.Answer).Trim();
            HashSet<string> known = new HashSet<string>(scope.Corpus.Documents.Select(d => d.Id), StringComparer.Ordinal);
            foreach (Match match in _Citation.Matches(item.Answer))
            {
                foreach (string part in match.Groups[1].Value.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (known.Contains(part) && !item.Cited.Contains(part)) item.Cited.Add(part);
                }
            }

            if (judge != null)
            {
                try
                {
                    item.Correct = await judge.GradeAsync(query.Text, item.Gold, item.Answer, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException e)
                {
                    item.Error = "judge: " + e.Message;
                }
            }

            return item;
        }

        private static List<KeyValuePair<ProvisionedScope, BenchmarkQuery>> Spread(List<KeyValuePair<ProvisionedScope, BenchmarkQuery>> work, int limit)
        {
            // Keep the type mix: take round-robin across query types.
            List<Queue<KeyValuePair<ProvisionedScope, BenchmarkQuery>>> queues = work
                .GroupBy(w => w.Value.Type)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Queue<KeyValuePair<ProvisionedScope, BenchmarkQuery>>(g))
                .ToList();
            List<KeyValuePair<ProvisionedScope, BenchmarkQuery>> picked = new List<KeyValuePair<ProvisionedScope, BenchmarkQuery>>();
            while (picked.Count < limit && queues.Any(q => q.Count > 0))
            {
                foreach (Queue<KeyValuePair<ProvisionedScope, BenchmarkQuery>> queue in queues)
                {
                    if (picked.Count >= limit) break;
                    if (queue.Count > 0) picked.Add(queue.Dequeue());
                }
            }

            return picked;
        }

        private static Dictionary<string, double> Summarize(List<ChatItem> items)
        {
            Dictionary<string, double> summary = new Dictionary<string, double>();
            List<ChatItem> ok = items.Where(i => i.StatusCode >= 200 && i.StatusCode < 300).ToList();
            List<ChatItem> answerable = ok.Where(i => i.Relevant.Count > 0).ToList();
            List<ChatItem> negatives = ok.Where(i => i.Relevant.Count == 0).ToList();

            summary["questions"] = items.Count;
            summary["errors"] = items.Count - ok.Count;
            summary["answerable"] = answerable.Count;
            summary["unanswerable"] = negatives.Count;

            List<ChatItem> judgedAnswerable = answerable.Where(i => i.Correct.HasValue).ToList();
            List<ChatItem> judgedNegative = negatives.Where(i => i.Correct.HasValue).ToList();
            if (judgedAnswerable.Count > 0) summary["accuracy"] = Math.Round(judgedAnswerable.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
            if (judgedNegative.Count > 0) summary["abstentionAccuracy"] = Math.Round(judgedNegative.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
            if (judgedAnswerable.Count + judgedNegative.Count > 0)
                summary["overallAccuracy"] = Math.Round(judgedAnswerable.Concat(judgedNegative).Average(i => i.Correct == true ? 1.0 : 0.0), 4);
            summary["unjudged"] = ok.Count(i => !i.Correct.HasValue);

            if (answerable.Count > 0)
            {
                summary["contextRecall"] = Math.Round(answerable.Average(i => (double)i.Retrieved.Intersect(i.Relevant).Count() / i.Relevant.Count), 4);
                List<ChatItem> cited = answerable.Where(i => i.Cited.Count > 0).ToList();
                summary["citationRate"] = Math.Round((double)cited.Count / answerable.Count, 4);
                if (cited.Count > 0) summary["citationPrecision"] = Math.Round(cited.Average(i => (double)i.Cited.Intersect(i.Relevant).Count() / i.Cited.Count), 4);
                summary["citationRecall"] = Math.Round(answerable.Average(i => (double)i.Cited.Intersect(i.Relevant).Count() / i.Relevant.Count), 4);

                // Accuracy when retrieval did / did not put the evidence in the prompt separates retrieval misses
                // from generation misses.
                List<ChatItem> hit = judgedAnswerable.Where(i => i.Retrieved.Intersect(i.Relevant).Any()).ToList();
                List<ChatItem> miss = judgedAnswerable.Where(i => !i.Retrieved.Intersect(i.Relevant).Any()).ToList();
                if (hit.Count > 0) summary["accuracyWhenEvidenceRetrieved"] = Math.Round(hit.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
                if (miss.Count > 0) summary["accuracyWhenEvidenceMissed"] = Math.Round(miss.Average(i => i.Correct == true ? 1.0 : 0.0), 4);
            }

            return summary;
        }

        private static string Format(Dictionary<string, double> metrics, string name)
        {
            return metrics.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion
    }
}
