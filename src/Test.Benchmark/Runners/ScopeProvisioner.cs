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

    /// <summary>
    /// Ingests benchmark corpora into Isis scopes. Scope names are deterministic (dataset, corpus, embedding
    /// configuration, optional suffix), so a later run reuses an already-ingested scope instead of re-embedding it
    /// unless --reingest is passed.
    /// </summary>
    public class ScopeProvisioner
    {
        #region Private-Members

        private readonly BenchmarkContext _Context;
        private readonly int _Concurrency;
        private readonly bool _Reingest;
        private readonly string _Suffix;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context (embedding endpoint must be prepared).</param>
        public ScopeProvisioner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Concurrency = Math.Max(1, context.Arguments.GetInt("ingest-concurrency", 8));
            _Reingest = context.Arguments.GetFlag("reingest");
            _Suffix = context.Arguments.Get("scope-suffix", string.Empty);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provision every corpus of a dataset.
        /// </summary>
        /// <param name="dataset">The dataset.</param>
        /// <param name="summary">Ingest statistics, accumulated across corpora.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioned scopes, in corpus order.</returns>
        public async Task<List<ProvisionedScope>> ProvisionAsync(BenchmarkDataset dataset, IngestSummary summary, CancellationToken token)
        {
            List<ProvisionedScope> scopes = new List<ProvisionedScope>();
            ConcurrentBag<double> latencies = new ConcurrentBag<double>();
            ConcurrentQueue<string> errors = new ConcurrentQueue<string>();
            int failures = 0;
            summary.Concurrency = _Concurrency;

            PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);
            Stopwatch wall = Stopwatch.StartNew();
            int corpusIndex = 0;

            foreach (BenchmarkCorpus corpus in dataset.Corpora)
            {
                corpusIndex++;
                string name = ScopeName(dataset, corpus);
                string? scopeId = await _Context.Client.FindScopeAsync(name, token).ConfigureAwait(false);

                if (scopeId != null && !_Reingest)
                {
                    // Count only the categories the corpus's documents live in, so memories a benchmark wrote on
                    // top (the load test's "load" category) do not force a needless re-ingest.
                    Dictionary<string, string> existing = await _Context.Client.ListCategoriesAsync(scopeId, token).ConfigureAwait(false);
                    long count = 0;
                    foreach (string categoryName in corpus.Documents.Select(d => d.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (existing.TryGetValue(categoryName, out string? categoryId))
                            count += await _Context.Client.CountMemoriesAsync(scopeId, categoryId, token).ConfigureAwait(false);
                    }

                    if (count == corpus.Documents.Count)
                    {
                        scopes.Add(new ProvisionedScope { Corpus = corpus, ScopeId = scopeId, Categories = existing });
                        summary.ReusedScopes++;
                        continue;
                    }
                }

                if (scopeId != null) await _Context.Client.DeleteScopeAsync(scopeId, token).ConfigureAwait(false);

                ProvisionedScope scope = new ProvisionedScope { Corpus = corpus };
                scope.ScopeId = await _Context.Client.CreateScopeAsync(ScopeDefinition(name, dataset, corpus), token).ConfigureAwait(false);
                foreach (BenchmarkCategory category in corpus.Categories)
                {
                    scope.Categories[category.Name] = await _Context.Client.CreateCategoryAsync(scope.ScopeId, category.Name, category.Description, token).ConfigureAwait(false);
                }

                foreach (string categoryName in corpus.Documents.Select(d => d.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!scope.Categories.ContainsKey(categoryName))
                        scope.Categories[categoryName] = await _Context.Client.CreateCategoryAsync(scope.ScopeId, categoryName, categoryName, token).ConfigureAwait(false);
                }

                using SemaphoreSlim gate = new SemaphoreSlim(_Concurrency);
                List<Task> tasks = new List<Task>();
                foreach (BenchmarkDocument document in corpus.Documents)
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            TimedResponse response = await _Context.Client.UpsertMemoryAsync(scope.ScopeId, MemoryBody(scope, document), token).ConfigureAwait(false);
                            latencies.Add(response.ElapsedMs);
                            if (!response.IsSuccess)
                            {
                                Interlocked.Increment(ref failures);
                                if (errors.Count < 10) errors.Enqueue(document.Id + ": " + response.StatusCode + " " + response.Error);
                            }
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
                summary.Documents += corpus.Documents.Count;
                scopes.Add(scope);

                if (dataset.Corpora.Count > 1)
                    Console.WriteLine("  ingested corpus " + corpusIndex + "/" + dataset.Corpora.Count + " (" + corpus.Documents.Count + " docs, " + wall.Elapsed.TotalSeconds.ToString("F0") + "s elapsed)");
            }

            wall.Stop();
            PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);

            summary.Failures += failures;
            summary.WallSeconds += Math.Round(wall.Elapsed.TotalSeconds, 2);
            summary.DocumentsPerSecond = summary.WallSeconds > 0 ? Math.Round(summary.Documents / summary.WallSeconds, 2) : 0.0;
            summary.Latency = LatencyStats.From(latencies);
            summary.Stages = after.Since(before);
            summary.SampleErrors.AddRange(errors);
            return scopes;
        }

        /// <summary>
        /// Delete the scopes created for a run.
        /// </summary>
        /// <param name="scopes">The scopes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CleanupAsync(IEnumerable<ProvisionedScope> scopes, CancellationToken token)
        {
            foreach (ProvisionedScope scope in scopes)
            {
                await _Context.Client.DeleteScopeAsync(scope.ScopeId, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private string ScopeName(BenchmarkDataset dataset, BenchmarkCorpus corpus)
        {
            string name = "bench-" + BenchmarkContext.Sanitize(dataset.Name) + "-" + BenchmarkContext.Sanitize(corpus.Id) + "-" + _Context.EmbeddingLabel;
            if (!string.IsNullOrEmpty(_Suffix)) name += "-" + BenchmarkContext.Sanitize(_Suffix);
            return name;
        }

        private JsonObject ScopeDefinition(string name, BenchmarkDataset dataset, BenchmarkCorpus corpus)
        {
            JsonObject definition = new JsonObject
            {
                ["name"] = name,
                ["description"] = "Benchmark scope: " + dataset.Name + " / " + corpus.Id,
                ["storeProvider"] = _Context.Arguments.Get("store", "RecallDb"),
                ["embeddingEndpointId"] = _Context.EmbeddingEndpointId,
                ["dimensionality"] = _Context.Dimensionality
            };

            string? chunkingMode = _Context.Arguments.GetOptional("chunking-mode");
            if (!string.IsNullOrEmpty(chunkingMode)) definition["chunkingMode"] = chunkingMode;
            string? strategy = _Context.Arguments.GetOptional("chunk-strategy");
            if (!string.IsNullOrEmpty(strategy)) definition["chunkStrategy"] = strategy;
            int chunkMax = _Context.Arguments.GetInt("chunk-max-tokens", -1);
            if (chunkMax >= 0) definition["chunkMaxTokens"] = chunkMax;
            int overlap = _Context.Arguments.GetInt("chunk-overlap", -1);
            if (overlap >= 0) definition["chunkOverlapTokens"] = overlap;
            return definition;
        }

        private static JsonObject MemoryBody(ProvisionedScope scope, BenchmarkDocument document)
        {
            JsonObject body = new JsonObject
            {
                ["slug"] = document.Id,
                ["categoryId"] = scope.Categories[document.Category],
                ["body"] = document.Body
            };
            if (!string.IsNullOrEmpty(document.Title)) body["title"] = document.Title;
            if (!string.IsNullOrEmpty(document.Summary)) body["summary"] = document.Summary;
            if (!string.IsNullOrEmpty(document.Date)) body["metadata"] = new JsonObject { ["date"] = document.Date };
            return body;
        }

        #endregion
    }
}
