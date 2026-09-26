namespace Test.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Agent;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Reporting;
    using Test.Benchmark.Runners;
    using Test.Benchmark.Stub;

    /// <summary>
    /// Isis benchmark harness. See benchmarks/README.md for the full workflow.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            BenchmarkArguments arguments = BenchmarkArguments.Parse(args);
            using CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                switch (arguments.Command)
                {
                    case "prepare":
                        return Prepare(arguments);
                    case "retrieval":
                        return await RetrievalAsync(arguments, cts.Token).ConfigureAwait(false);
                    case "load":
                        return await LoadAsync(arguments, cts.Token).ConfigureAwait(false);
                    case "chat":
                        return await ChatAsync(arguments, cts.Token).ConfigureAwait(false);
                    case "agent":
                        return await AgentAsync(arguments, cts.Token).ConfigureAwait(false);
                    case "stub":
                        return await StubAsync(arguments, cts.Token).ConfigureAwait(false);
                    case "compare":
                        return ResultComparer.Compare(arguments);
                    case "history":
                        return History(arguments);
                    default:
                        PrintUsage();
                        return string.IsNullOrEmpty(arguments.Command) || arguments.Command == "help" ? 0 : 2;
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Cancelled.");
                return 130;
            }
            catch (Exception e) when (e is InvalidOperationException || e is InvalidDataException || e is FileNotFoundException || e is ArgumentException)
            {
                Console.Error.WriteLine("Error: " + e.Message);
                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static int Prepare(BenchmarkArguments arguments)
        {
            string format = arguments.Get("format", string.Empty).ToLowerInvariant();
            string input = arguments.Get("input", string.Empty);
            string output = arguments.Get("output", string.Empty);
            if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                throw new ArgumentException("prepare needs --format beir|longmemeval --input <path> --output <file.json>.");

            BenchmarkDataset dataset;
            if (format == "beir")
            {
                dataset = BeirConverter.Convert(input, arguments.Get("name", Path.GetFileName(input.TrimEnd('/', '\\'))), arguments.Get("split", "test"));
            }
            else if (format == "longmemeval")
            {
                dataset = LongMemEvalConverter.Convert(input, arguments.Get("name", "longmemeval-s"), arguments.GetInt("limit", 0), arguments.GetInt("seed", 7));
            }
            else
            {
                throw new ArgumentException("Unknown --format '" + format + "' (beir, longmemeval).");
            }

            DatasetStore.Save(dataset, output);
            Console.WriteLine("Wrote " + output + ": " + dataset.Description);
            return 0;
        }

        private static async Task<int> RetrievalAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(RequireDataset(arguments));
            using BenchmarkContext context = new BenchmarkContext(arguments);
            await context.PrepareEmbeddingAsync(token).ConfigureAwait(false);

            RetrievalReport report = await new RetrievalRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
            string suffix = arguments.GetOptional("label") ?? arguments.GetOptional("scope-suffix") ?? string.Empty;
            string basePath = context.ResultPath("retrieval", dataset.Name + (suffix.Length > 0 ? "-" + suffix : string.Empty));
            Console.WriteLine("Wrote " + ReportWriter.WriteJson(report, basePath));
            Console.WriteLine("Wrote " + ReportWriter.WriteMarkdown(ReportWriter.RenderRetrieval(report, LoadBaselines(arguments)), basePath));
            return 0;
        }

        private static BaselineCatalog LoadBaselines(BenchmarkArguments arguments)
        {
            return BaselineCatalog.Load(arguments.Get("baselines", Path.Combine(BenchmarkContext.RepositoryRoot(), "benchmarks", "baselines.json")));
        }

        private static int History(BenchmarkArguments arguments)
        {
            // Build a round-over-round overview from saved reports, for example:
            //   history --rounds R1=20260924-17,R2=final,R3=r3,R4=r4,R5=r5 --chat-rounds R3=r3,R4=r4,R5=r5
            List<RoundSelector> rounds = HistoryWriter.ParseRounds(arguments.Get("rounds", string.Empty));
            if (rounds.Count == 0) throw new ArgumentException("history needs --rounds NAME=LABEL,... (a run label or a UTC timestamp prefix per round).");
            List<RoundSelector> chatRounds = HistoryWriter.ParseRounds(arguments.Get("chat-rounds", string.Empty));

            string directory = Path.GetFullPath(arguments.Get("out", Path.Combine(BenchmarkContext.RepositoryRoot(), "benchmarks", "results")));
            List<string> datasets = arguments.GetList("datasets", "isis-live,atlas,scifact,longmemeval-s");
            string markdown = HistoryWriter.Render(directory, rounds, datasets, arguments.Get("mode", "Hybrid"), arguments.Get("metric", "ndcg@10"), LoadBaselines(arguments), chatRounds, arguments.Get("chat-dataset", "isis-live"));

            string label = arguments.GetOptional("label") ?? "history";
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + BenchmarkContext.Sanitize(label) + ".md");
            File.WriteAllText(path, markdown, new UTF8Encoding(false));
            Console.WriteLine(markdown);
            Console.WriteLine("Wrote " + path);
            return 0;
        }

        private static async Task<int> LoadAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            string? datasetPath = arguments.GetOptional("dataset");
            BenchmarkDataset? source = string.IsNullOrEmpty(datasetPath) ? null : DatasetStore.Load(datasetPath);
            using BenchmarkContext context = new BenchmarkContext(arguments);

            LoadReport report = await new LoadRunner(context).RunAsync(source, token).ConfigureAwait(false);
            string basePath = context.ResultPath("load", report.Scenario + "-" + report.CorpusSize + (arguments.GetFlag("stub") ? "-stub" : string.Empty));
            Console.WriteLine("Wrote " + ReportWriter.WriteJson(report, basePath));
            Console.WriteLine("Wrote " + ReportWriter.WriteMarkdown(ReportWriter.RenderLoad(report), basePath));
            return 0;
        }

        private static async Task<int> ChatAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            BenchmarkDataset dataset = DatasetStore.Load(RequireDataset(arguments));
            using BenchmarkContext context = new BenchmarkContext(arguments);
            await context.PrepareEmbeddingAsync(token).ConfigureAwait(false);

            ChatReport report = await new ChatRunner(context).RunAsync(dataset, token).ConfigureAwait(false);
            string basePath = context.ResultPath("chat", dataset.Name + (arguments.GetOptional("label") != null ? "-" + arguments.Get("label", string.Empty) : string.Empty));
            Console.WriteLine("Wrote " + ReportWriter.WriteJson(report, basePath));
            Console.WriteLine("Wrote " + ReportWriter.WriteMarkdown(ReportWriter.RenderChat(report), basePath));
            return 0;
        }

        private static async Task<int> AgentAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            string tasks = arguments.Get("tasks", string.Empty);
            if (!File.Exists(tasks)) throw new FileNotFoundException("--tasks <file.json> is required and must exist.");
            using BenchmarkContext context = new BenchmarkContext(arguments);
            await context.PrepareEmbeddingAsync(token).ConfigureAwait(false);

            AgentReport report = await new AgentRunner(context).RunAsync(tasks, token).ConfigureAwait(false);
            string basePath = context.ResultPath("agent", report.Suite + "-" + arguments.Get("model", "haiku"));
            Console.WriteLine("Wrote " + ReportWriter.WriteJson(report, basePath));
            Console.WriteLine("Wrote " + ReportWriter.WriteMarkdown(ReportWriter.RenderAgent(report), basePath));
            return 0;
        }

        private static async Task<int> StubAsync(BenchmarkArguments arguments, CancellationToken token)
        {
            await using StubEmbeddingServer stub = new StubEmbeddingServer(arguments.GetInt("stub-port", 18900), arguments.GetInt("dim", 384), arguments.GetInt("stub-latency-ms", 5));
            await stub.StartAsync(token).ConfigureAwait(false);
            Console.WriteLine("Stub embeddings listening on " + stub.BaseUrl + " (Ctrl+C to stop)");
            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Console.WriteLine("Served " + stub.Requests + " requests.");
            return 0;
        }

        private static string RequireDataset(BenchmarkArguments arguments)
        {
            string path = arguments.Get("dataset", string.Empty);
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("--dataset <file.json> is required.");
            if (!File.Exists(path)) throw new FileNotFoundException("Dataset not found: " + path);
            return path;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Isis benchmark harness");
            Console.WriteLine();
            Console.WriteLine("  prepare   --format beir|longmemeval --input <dir|file> --output <file.json> [--limit N --seed S --split test]");
            Console.WriteLine("  retrieval --dataset <file.json> [--modes Keyword,Semantic,Hybrid] [--k 10] [--concurrency 1] [--no-category]");
            Console.WriteLine("            [--reingest] [--ingest-concurrency 8] [--cleanup] [--scope-suffix x]");
            Console.WriteLine("            [--chunking-mode OnOverflow|Always|Off] [--chunk-max-tokens N] [--chunk-overlap N]");
            Console.WriteLine("            [--recency-weight 0..1] [--min-score X]   (hybrid recency ablation; score threshold)");
            Console.WriteLine("  load      [--dataset <file.json>] [--corpus-size 1000] [--concurrency 1,4,16] [--duration 30] [--warmup 5]");
            Console.WriteLine("            [--scenario mixed|search|upsert] [--search-weight 0.9] [--long-fraction 0.2] [--mode Hybrid]");
            Console.WriteLine("            [--stub --stub-latency-ms 5 --stub-port 18900]   (isolate Isis+RecallDB from the embedding model)");
            Console.WriteLine("  chat      --dataset <file.json> [--k N (default: server default)] [--limit N] [--inference-url/-format/-model] [--judge-url/-format/-model | --judge-format none]");
            Console.WriteLine("  agent     --tasks <tasks.json> [--model haiku] [--arms isis,none] [--mcp-url http://127.0.0.1:18720/mcp] [--limit N]");
            Console.WriteLine("  stub      [--stub-port 18900] [--stub-latency-ms 5] [--dim 384]   (standalone stub embedding server)");
            Console.WriteLine("  compare   --baseline <a.json> --candidate <b.json> [--tolerance 0.01] [--latency-tolerance 0.2]");
            Console.WriteLine("  history   --rounds R1=<label|timestamp-prefix>,R2=... [--chat-rounds R1=...] [--datasets isis-live,atlas,scifact,longmemeval-s]");
            Console.WriteLine("            [--mode Hybrid] [--metric ndcg@10] [--label history]   (per-type table per round, net change, published baselines)");
            Console.WriteLine();
            Console.WriteLine("Common: --url http://127.0.0.1:18700 --access-key isisdefaultkey --metrics-url http://127.0.0.1:19464/metrics|none");
            Console.WriteLine("        --embedding-url http://127.0.0.1:11434 --embedding-format Ollama --embedding-model all-minilm --dim 384");
            Console.WriteLine("        [--max-input-tokens N] [--embedding-endpoint-id eep_...] [--out benchmarks/results]");
        }

        #endregion
    }
}
