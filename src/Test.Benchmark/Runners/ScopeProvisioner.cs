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
    /// <remarks>
    /// A corpus whose documents carry dates is written strictly one document at a time in date order, so each
    /// memory's write time (which Isis uses as its recency signal) follows the order the facts were recorded.
    /// Throughput then comes from provisioning several corpora at once (LongMemEval has one corpus per question). A
    /// single undated corpus uses document-level parallelism instead.
    /// </remarks>
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
            ProvisionedScope?[] scopes = new ProvisionedScope?[dataset.Corpora.Count];
            ConcurrentBag<double> latencies = new ConcurrentBag<double>();
            ConcurrentQueue<string> errors = new ConcurrentQueue<string>();
            ConcurrentQueue<KeyValuePair<string, string>> flags = new ConcurrentQueue<KeyValuePair<string, string>>();
            int failures = 0;
            int documents = 0;
            int reused = 0;
            int completed = 0;
            summary.Concurrency = _Concurrency;

            bool multiCorpus = dataset.Corpora.Count > 1;
            int corpusConcurrency = multiCorpus ? _Concurrency : 1;

            PrometheusSnapshot before = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);
            Stopwatch wall = Stopwatch.StartNew();

            using SemaphoreSlim corpusGate = new SemaphoreSlim(corpusConcurrency);
            List<Task> corpusTasks = new List<Task>();
            for (int index = 0; index < dataset.Corpora.Count; index++)
            {
                int corpusIndex = index;
                BenchmarkCorpus corpus = dataset.Corpora[index];
                await corpusGate.WaitAsync(token).ConfigureAwait(false);
                corpusTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        ProvisionedScope? existing = await TryReuseAsync(dataset, corpus, token).ConfigureAwait(false);
                        if (existing != null)
                        {
                            scopes[corpusIndex] = existing;
                            Interlocked.Increment(ref reused);
                            return;
                        }

                        ProvisionedScope scope = await CreateScopeAsync(dataset, corpus, token).ConfigureAwait(false);
                        bool dated = corpus.Documents.Any(d => !string.IsNullOrEmpty(d.Date));
                        IEnumerable<BenchmarkDocument> writeOrder = dated
                            ? corpus.Documents.OrderBy(d => d.Date ?? string.Empty, StringComparer.Ordinal)
                            : corpus.Documents;
                        int documentConcurrency = dated || multiCorpus ? 1 : _Concurrency;

                        using SemaphoreSlim gate = new SemaphoreSlim(documentConcurrency);
                        List<Task> tasks = new List<Task>();
                        foreach (BenchmarkDocument document in writeOrder)
                        {
                            await gate.WaitAsync(token).ConfigureAwait(false);
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    UpsertResponse response = await _Context.Client.UpsertMemoryAsync(scope.ScopeId, MemoryBody(scope, document), token).ConfigureAwait(false);
                                    latencies.Add(response.ElapsedMs);
                                    foreach (string similar in response.SimilarSlugs) flags.Enqueue(new KeyValuePair<string, string>(document.Id, similar));
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
                        Interlocked.Add(ref documents, corpus.Documents.Count);
                        scopes[corpusIndex] = scope;
                        int done = Interlocked.Increment(ref completed);
                        if (multiCorpus && done % 10 == 0)
                            Console.WriteLine("  ingested " + done + " corpora (" + wall.Elapsed.TotalSeconds.ToString("F0") + "s elapsed)");
                    }
                    finally
                    {
                        corpusGate.Release();
                    }
                }, token));
            }

            await Task.WhenAll(corpusTasks).ConfigureAwait(false);
            wall.Stop();
            PrometheusSnapshot after = await PrometheusSnapshot.CaptureAsync(_Context.Http, _Context.MetricsUrl, token).ConfigureAwait(false);

            summary.Documents += documents;
            summary.ReusedScopes += reused;
            summary.Failures += failures;
            summary.WallSeconds += Math.Round(wall.Elapsed.TotalSeconds, 2);
            summary.DocumentsPerSecond = summary.WallSeconds > 0 ? Math.Round(summary.Documents / summary.WallSeconds, 2) : 0.0;
            summary.Latency = LatencyStats.From(latencies);
            summary.Stages = after.Since(before);
            summary.SampleErrors.AddRange(errors);
            ScoreSimilarityFlags(dataset, flags.ToList(), summary);

            List<ProvisionedScope> provisioned = scopes.Select(s => s!).ToList();
            await ConfigureRerankAsync(provisioned, token).ConfigureAwait(false);
            return provisioned;
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

        private async Task ConfigureRerankAsync(List<ProvisionedScope> scopes, CancellationToken token)
        {
            // Every run sets the rerank settings explicitly, so a reused scope never keeps a previous run's reranker.
            string? endpointId = _Context.RerankEndpointId;
            int candidates = Math.Max(1, _Context.Arguments.GetInt("rerank-candidates", 20));
            double? minScore = _Context.Arguments.GetOptional("scope-min-rerank-score") != null ? _Context.Arguments.GetDouble("scope-min-rerank-score", 0.0) : (double?)null;
            foreach (ProvisionedScope scope in scopes)
            {
                await _Context.Client.ConfigureRerankAsync(scope.ScopeId, endpointId, candidates, minScore, token).ConfigureAwait(false);
            }
        }

        private static void ScoreSimilarityFlags(BenchmarkDataset dataset, List<KeyValuePair<string, string>> flags, IngestSummary summary)
        {
            // A known pair (replacement, replaced) counts as detected when writing either one flagged the other.
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (BenchmarkDocument document in dataset.Corpora.SelectMany(c => c.Documents))
            {
                foreach (string replaced in document.Supersedes ?? new List<string>()) known.Add(PairKey(document.Id, replaced));
            }

            HashSet<string> flagged = new HashSet<string>(flags.Select(f => PairKey(f.Key, f.Value)), StringComparer.Ordinal);
            summary.SimilarFlags = flags.Count;
            summary.SupersessionPairs = known.Count;
            summary.SupersessionPairsFlagged = known.Count(k => flagged.Contains(k));
            summary.SimilarFlagsOnKnownPairs = flagged.Count(k => known.Contains(k));
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        }

        private async Task<ProvisionedScope?> TryReuseAsync(BenchmarkDataset dataset, BenchmarkCorpus corpus, CancellationToken token)
        {
            string? scopeId = await _Context.Client.FindScopeAsync(ScopeName(dataset, corpus), token).ConfigureAwait(false);
            if (scopeId == null) return null;

            if (!_Reingest)
            {
                // Count only the categories the corpus's documents live in, so memories a benchmark wrote on top (the
                // load test's "load" category) do not force a needless re-ingest.
                Dictionary<string, string> existing = await _Context.Client.ListCategoriesAsync(scopeId, token).ConfigureAwait(false);
                long count = 0;
                foreach (string categoryName in corpus.Documents.Select(d => d.Category).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (existing.TryGetValue(categoryName, out string? categoryId))
                        count += await _Context.Client.CountMemoriesAsync(scopeId, categoryId, token).ConfigureAwait(false);
                }

                if (count == corpus.Documents.Count) return new ProvisionedScope { Corpus = corpus, ScopeId = scopeId, Categories = existing };
            }

            await _Context.Client.DeleteScopeAsync(scopeId, token).ConfigureAwait(false);
            return null;
        }

        private async Task<ProvisionedScope> CreateScopeAsync(BenchmarkDataset dataset, BenchmarkCorpus corpus, CancellationToken token)
        {
            ProvisionedScope scope = new ProvisionedScope { Corpus = corpus };
            scope.ScopeId = await _Context.Client.CreateScopeAsync(ScopeDefinition(ScopeName(dataset, corpus), dataset, corpus), token).ConfigureAwait(false);
            foreach (BenchmarkCategory category in corpus.Categories)
            {
                scope.Categories[category.Name] = await _Context.Client.CreateCategoryAsync(scope.ScopeId, category.Name, category.Description, token).ConfigureAwait(false);
            }

            foreach (string categoryName in corpus.Documents.Select(d => d.Category).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!scope.Categories.ContainsKey(categoryName))
                    scope.Categories[categoryName] = await _Context.Client.CreateCategoryAsync(scope.ScopeId, categoryName, categoryName, token).ConfigureAwait(false);
            }

            return scope;
        }

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
            if (document.Supersedes != null && document.Supersedes.Count > 0)
            {
                JsonArray supersedes = new JsonArray();
                foreach (string replaced in document.Supersedes) supersedes.Add(replaced);
                body["supersedes"] = supersedes;
            }

            return body;
        }

        #endregion
    }
}
