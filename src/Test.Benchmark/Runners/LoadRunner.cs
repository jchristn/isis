namespace Test.Benchmark.Runners
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Stub;

    /// <summary>
    /// Closed-loop load test. A corpus of the requested size is preloaded into a scope, then for each concurrency
    /// level N workers issue a weighted mix of searches, short upserts, and long (chunked) upserts for a fixed
    /// duration, after a warmup. Reports throughput, latency percentiles, error rate, and the server stage breakdown
    /// per level, so the saturation point and the dominant stage are visible.
    /// </summary>
    public class LoadRunner
    {
        #region Private-Members

        private readonly BenchmarkContext _Context;
        private const int _UpsertSlugPool = 500;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        public LoadRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the load test.
        /// </summary>
        /// <param name="source">Optional dataset used as text templates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<LoadReport> RunAsync(BenchmarkDataset? source, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            string scenario = args.Get("scenario", "mixed").ToLowerInvariant();
            int corpusSize = args.GetInt("corpus-size", 1000);
            int seconds = args.GetInt("duration", 30);
            int warmup = args.GetInt("warmup", 5);
            List<int> levels = args.GetList("concurrency", "1,4,16").Select(int.Parse).ToList();
            string mode = args.Get("mode", "Hybrid");
            int topK = args.GetInt("k", 10);
            double searchWeight = scenario == "search" ? 1.0 : scenario == "upsert" ? 0.0 : args.GetDouble("search-weight", 0.9);
            double longFraction = args.GetDouble("long-fraction", 0.2);

            StubEmbeddingServer? stub = null;
            try
            {
                if (args.GetFlag("stub"))
                {
                    int port = args.GetInt("stub-port", 18900);
                    int latency = args.GetInt("stub-latency-ms", 5);
                    stub = new StubEmbeddingServer(port, args.GetInt("dim", 384), latency);
                    await stub.StartAsync(token).ConfigureAwait(false);
                    Console.WriteLine("[load] stub embeddings on " + stub.BaseUrl + " (" + latency + " ms per call)");
                }

                await PrepareEmbeddingAsync(stub, token).ConfigureAwait(false);

                SyntheticCorpus corpus = SyntheticCorpus.Build(source, corpusSize, args.GetInt("seed", 7));
                LoadReport report = new LoadReport { Scenario = scenario, Environment = _Context.Environment, CorpusSize = corpusSize };
                report.Config["scenario"] = scenario;
                report.Config["mode"] = mode;
                report.Config["topK"] = topK.ToString();
                report.Config["searchWeight"] = searchWeight.ToString("F2");
                report.Config["longFraction"] = longFraction.ToString("F2");
                report.Config["durationSeconds"] = seconds.ToString();
                report.Config["warmupSeconds"] = warmup.ToString();
                report.Config["templates"] = source != null ? source.Name : "synthetic vocabulary";

                Console.WriteLine("[load] preloading " + corpusSize + " memories");
                ScopeProvisioner provisioner = new ScopeProvisioner(_Context);
                List<ProvisionedScope> scopes = await provisioner.ProvisionAsync(corpus.Dataset, report.Ingest, token).ConfigureAwait(false);
                ProvisionedScope scope = scopes[0];
                Console.WriteLine("[load] preload: " + report.Ingest.Documents + " docs in " + report.Ingest.WallSeconds + "s (" + report.Ingest.DocumentsPerSecond + " docs/s), reused " + report.Ingest.ReusedScopes);

                foreach (int concurrency in levels)
                {
                    if (warmup > 0) await RunPhaseAsync(scope, corpus, concurrency, warmup, mode, topK, searchWeight, longFraction, null, token).ConfigureAwait(false);

                    ConcurrentBag<LoadSample> samples = new ConcurrentBag<LoadSample>();
                    PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);
                    double elapsed = await RunPhaseAsync(scope, corpus, concurrency, seconds, mode, topK, searchWeight, longFraction, samples, token).ConfigureAwait(false);
                    PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);

                    LoadLevel level = Summarize(concurrency, elapsed, samples);
                    level.Stages = after.Since(before);
                    report.Levels.Add(level);
                    Console.WriteLine("[load] c=" + concurrency.ToString().PadRight(4) + " " + level.Throughput.ToString("F1") + " ops/s, errors " + (level.ErrorRate * 100).ToString("F1") + "%  "
                        + string.Join("  ", level.Operations.Select(o => o.Key + " p50 " + o.Value.Latency.P50 + " p95 " + o.Value.Latency.P95 + " p99 " + o.Value.Latency.P99)));
                }

                if (args.GetFlag("cleanup")) await provisioner.CleanupAsync(scopes, token).ConfigureAwait(false);
                return report;
            }
            finally
            {
                if (stub != null) await stub.DisposeAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task PrepareEmbeddingAsync(StubEmbeddingServer? stub, CancellationToken token)
        {
            if (stub == null)
            {
                await _Context.PrepareEmbeddingAsync(token).ConfigureAwait(false);
                return;
            }

            // Route the context's embedding endpoint at the stub. The label keeps stub scopes apart from real-model scopes.
            BenchmarkArguments args = _Context.Arguments;
            BenchmarkArguments stubArgs = BenchmarkArguments.Parse(new string[]
            {
                "load",
                "--embedding-format", "Ollama",
                "--embedding-model", "stub",
                "--embedding-url", stub.BaseUrl,
                "--embedding-label", "stub",
                "--dim", args.Get("dim", "384")
            });
            await _Context.PrepareEmbeddingAsync(token, stubArgs).ConfigureAwait(false);
        }

        private async Task<double> RunPhaseAsync(
            ProvisionedScope scope,
            SyntheticCorpus corpus,
            int concurrency,
            int seconds,
            string mode,
            int topK,
            double searchWeight,
            double longFraction,
            ConcurrentBag<LoadSample>? samples,
            CancellationToken token)
        {
            string loadCategory = scope.Categories["load"];
            long deadline = Stopwatch.GetTimestamp() + (long)(seconds * (double)Stopwatch.Frequency);
            Stopwatch wall = Stopwatch.StartNew();
            List<Task> workers = new List<Task>();

            for (int w = 0; w < concurrency; w++)
            {
                int workerSeed = w * 7919 + concurrency;
                workers.Add(Task.Run(async () =>
                {
                    Random random = new Random(workerSeed);
                    // Workers stop starting new operations at the deadline but let in-flight calls finish, so no
                    // request is cancelled mid-flight and counted as an error.
                    while (Stopwatch.GetTimestamp() < deadline && !token.IsCancellationRequested)
                    {
                        LoadSample sample;
                        if (random.NextDouble() < searchWeight)
                        {
                            string query = corpus.Queries[random.Next(corpus.Queries.Count)];
                            SearchResponse response = await _Context.Client.SearchAsync(scope.ScopeId, query, mode, topK, null, token).ConfigureAwait(false);
                            sample = new LoadSample { Operation = "search", ElapsedMs = response.ElapsedMs, Success = response.IsSuccess, Error = response.Error };
                        }
                        else
                        {
                            bool isLong = random.NextDouble() < longFraction;
                            string body = isLong ? corpus.LongBodies[random.Next(corpus.LongBodies.Count)] : corpus.ShortBodies[random.Next(corpus.ShortBodies.Count)];
                            JsonObject memory = new JsonObject
                            {
                                ["slug"] = "load-" + random.Next(_UpsertSlugPool),
                                ["categoryId"] = loadCategory,
                                ["title"] = "Load test memory",
                                ["body"] = body
                            };
                            TimedResponse response = await _Context.Client.UpsertMemoryAsync(scope.ScopeId, memory, token).ConfigureAwait(false);
                            sample = new LoadSample { Operation = isLong ? "upsert-long" : "upsert-short", ElapsedMs = response.ElapsedMs, Success = response.IsSuccess, Error = response.Error };
                        }

                        samples?.Add(sample);
                    }
                }, token));
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            return wall.Elapsed.TotalSeconds;
        }

        private static LoadLevel Summarize(int concurrency, double seconds, ConcurrentBag<LoadSample> samples)
        {
            List<LoadSample> all = samples.ToList();
            LoadLevel level = new LoadLevel
            {
                Concurrency = concurrency,
                Seconds = Math.Round(seconds, 2),
                Throughput = seconds > 0 ? Math.Round(all.Count / seconds, 2) : 0.0,
                ErrorRate = all.Count > 0 ? Math.Round((double)all.Count(s => !s.Success) / all.Count, 4) : 0.0
            };

            foreach (IGrouping<string, LoadSample> group in all.GroupBy(s => s.Operation).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<LoadSample> ok = group.Where(s => s.Success).ToList();
                level.Operations[group.Key] = new LoadOpStats
                {
                    Count = group.Count(),
                    Errors = group.Count(s => !s.Success),
                    Throughput = seconds > 0 ? Math.Round(group.Count() / seconds, 2) : 0.0,
                    Latency = LatencyStats.From(ok.Select(s => s.ElapsedMs))
                };
            }

            level.SampleErrors.AddRange(all.Where(s => !s.Success).Select(s => s.Operation + ": " + s.Error).Distinct().Take(5));
            return level;
        }

        #endregion
    }
}
