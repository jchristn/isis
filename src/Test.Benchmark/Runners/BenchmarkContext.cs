namespace Test.Benchmark.Runners
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Settings and connections shared by every benchmark command: the Isis client, the embedding endpoint the
    /// benchmark scopes use, the metrics URL, and where results are written.
    /// </summary>
    public class BenchmarkContext : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The parsed arguments.
        /// </summary>
        public BenchmarkArguments Arguments { get; }

        /// <summary>
        /// The Isis client.
        /// </summary>
        public IsisClient Client { get; }

        /// <summary>
        /// A plain HTTP client for metrics scrapes and direct model calls.
        /// </summary>
        public HttpClient Http { get; } = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>
        /// Embedding endpoint id used for benchmark scopes (set by <see cref="PrepareEmbeddingAsync"/>).
        /// </summary>
        public string EmbeddingEndpointId { get; private set; } = string.Empty;

        /// <summary>
        /// Embedding dimensionality.
        /// </summary>
        public int Dimensionality { get; private set; } = 384;

        /// <summary>
        /// Short label for the embedding configuration (used in scope names so different models never share a scope).
        /// </summary>
        public string EmbeddingLabel { get; private set; } = string.Empty;

        /// <summary>
        /// Prometheus metrics URL, or null when disabled.
        /// </summary>
        public string? MetricsUrl { get; }

        /// <summary>
        /// Directory results are written to.
        /// </summary>
        public string OutputDirectory { get; }

        /// <summary>
        /// The captured environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; private set; } = new BenchmarkEnvironment();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from arguments.
        /// </summary>
        /// <param name="arguments">Parsed arguments.</param>
        public BenchmarkContext(BenchmarkArguments arguments)
        {
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
            string url = arguments.Get("url", "http://127.0.0.1:18700");
            Client = new IsisClient(url, arguments.Get("access-key", "isisdefaultkey"));

            string metrics = arguments.Get("metrics-url", "http://127.0.0.1:19464/metrics");
            MetricsUrl = string.Equals(metrics, "none", StringComparison.OrdinalIgnoreCase) ? null : metrics;
            OutputDirectory = Path.GetFullPath(arguments.Get("out", Path.Combine(RepositoryRoot(), "benchmarks", "results")));
            Directory.CreateDirectory(OutputDirectory);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connect to Isis and create or update the benchmark embedding endpoint.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="overrides">Optional arguments to read the embedding options from instead of the command line.</param>
        /// <returns>Task.</returns>
        public async Task PrepareEmbeddingAsync(CancellationToken token, BenchmarkArguments? overrides = null)
        {
            await Client.ConnectAsync(token).ConfigureAwait(false);
            BenchmarkArguments source = overrides ?? Arguments;

            string format = source.Get("embedding-format", "Ollama");
            string model = source.Get("embedding-model", "all-minilm");
            string baseUrl = source.Get("embedding-url", "http://127.0.0.1:11434");
            Dimensionality = source.GetInt("dim", 384);
            int maxInputTokens = source.GetInt("max-input-tokens", 0);

            string? existing = source.GetOptional("embedding-endpoint-id");
            if (!string.IsNullOrEmpty(existing))
            {
                EmbeddingEndpointId = existing;
                EmbeddingLabel = source.Get("embedding-label", "custom");
            }
            else
            {
                EmbeddingLabel = source.Get("embedding-label", Sanitize(model) + (maxInputTokens > 0 ? "-t" + maxInputTokens : string.Empty));
                JsonObject definition = new JsonObject
                {
                    ["name"] = "bench-embed-" + EmbeddingLabel,
                    ["kind"] = "Embedding",
                    ["apiFormat"] = format,
                    ["baseUrl"] = baseUrl,
                    ["model"] = model,
                    ["dimensionality"] = Dimensionality,
                    ["maxInputTokens"] = maxInputTokens,
                    ["timeoutMs"] = 120000
                };
                EmbeddingEndpointId = await Client.EnsureEndpointAsync(definition, token).ConfigureAwait(false);
            }

            Environment = BenchmarkEnvironment.Capture(Client.BaseUrl, format + " " + model + " @ " + baseUrl + " (dim " + Dimensionality + ", maxInputTokens " + maxInputTokens + ")");
        }

        /// <summary>
        /// Create or update an inference endpoint from the --inference-* arguments.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint id.</returns>
        public async Task<string> PrepareInferenceAsync(CancellationToken token)
        {
            string? existing = Arguments.GetOptional("inference-endpoint-id");
            if (!string.IsNullOrEmpty(existing)) return existing;

            string format = Arguments.Get("inference-format", "Ollama");
            string model = Arguments.Get("inference-model", "gemma3:4b");
            string baseUrl = Arguments.Get("inference-url", "http://127.0.0.1:11434");
            JsonObject definition = new JsonObject
            {
                ["name"] = "bench-infer-" + Sanitize(model),
                ["kind"] = "Inference",
                ["apiFormat"] = format,
                ["baseUrl"] = baseUrl,
                ["model"] = model,
                ["timeoutMs"] = 600000
            };
            Environment.Inference = format + " " + model + " @ " + baseUrl;
            return await Client.EnsureEndpointAsync(definition, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build a timestamped result file base path.
        /// </summary>
        /// <param name="kind">Benchmark kind (retrieval, load, chat, agent).</param>
        /// <param name="name">Dataset or scenario name.</param>
        /// <returns>Path without extension.</returns>
        public string ResultPath(string kind, string name)
        {
            string stamp = Environment.StartedUtc.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(OutputDirectory, stamp + "-" + kind + "-" + Sanitize(name));
        }

        /// <summary>
        /// Make a string safe for scope names and file names.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>Lower-case letters, digits, and dashes.</returns>
        public static string Sanitize(string value)
        {
            char[] chars = (value ?? string.Empty).ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '-';
            }

            return new string(chars).Trim('-');
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Client.Dispose();
            Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) && Directory.Exists(Path.Combine(directory.FullName, "benchmarks")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            return Directory.GetCurrentDirectory();
        }

        #endregion
    }
}
