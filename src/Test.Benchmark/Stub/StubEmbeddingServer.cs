namespace Test.Benchmark.Stub
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A deterministic embedding server with a fixed, configurable latency. Load tests point Isis at it so they
    /// measure Isis and RecallDB rather than the embedding model (whose throughput would otherwise dominate).
    /// Vectors come from feature hashing over lower-cased word tokens, so texts that share words still get similar
    /// vectors and search results stay meaningful. Speaks the Ollama (/api/embeddings, /api/embed) and OpenAI
    /// (/v1/embeddings) formats.
    /// </summary>
    public class StubEmbeddingServer : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL the server listens on.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Embedding requests served.
        /// </summary>
        public long Requests
        {
            get
            {
                return Interlocked.Read(ref _Requests);
            }
        }

        #endregion

        #region Private-Members

        private readonly int _Dimensionality;
        private readonly int _LatencyMs;
        private readonly WebApplication _App;
        private long _Requests = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (call <see cref="StartAsync"/> to listen).
        /// </summary>
        /// <param name="port">Loopback port.</param>
        /// <param name="dimensionality">Vector dimensionality.</param>
        /// <param name="latencyMs">Added latency per request in milliseconds.</param>
        public StubEmbeddingServer(int port, int dimensionality, int latencyMs)
        {
            if (dimensionality < 1) throw new ArgumentOutOfRangeException(nameof(dimensionality));
            _Dimensionality = dimensionality;
            _LatencyMs = Math.Max(0, latencyMs);
            BaseUrl = "http://127.0.0.1:" + port;

            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(BaseUrl);
            builder.Logging.ClearProviders();
            _App = builder.Build();
            _App.MapGet("/", () => "stub embeddings ok");
            _App.MapPost("/api/embeddings", OllamaSingleAsync);
            _App.MapPost("/api/embed", OllamaBatchAsync);
            _App.MapPost("/v1/embeddings", OpenAiAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start listening.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token)
        {
            await _App.StartAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Compute the stub embedding for a text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>An L2-normalized vector.</returns>
        public float[] Embed(string text)
        {
            float[] vector = new float[_Dimensionality];
            int start = -1;
            string lower = (text ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i <= lower.Length; i++)
            {
                bool wordChar = i < lower.Length && char.IsLetterOrDigit(lower[i]);
                if (wordChar && start < 0) start = i;
                if (!wordChar && start >= 0)
                {
                    uint hash = Fnv(lower, start, i - start);
                    int index = (int)(hash % (uint)_Dimensionality);
                    vector[index] += (hash & 0x80000000u) != 0 ? -1.0f : 1.0f;
                    start = -1;
                }
            }

            double norm = 0.0;
            foreach (float v in vector) norm += v * v;
            if (norm <= 0.0)
            {
                vector[0] = 1.0f;
                return vector;
            }

            float scale = (float)(1.0 / Math.Sqrt(norm));
            for (int i = 0; i < vector.Length; i++) vector[i] *= scale;
            return vector;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _App.StopAsync().ConfigureAwait(false);
            await _App.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task OllamaSingleAsync(HttpContext context)
        {
            JsonNode? body = await ReadAsync(context).ConfigureAwait(false);
            string prompt = body?["prompt"]?.GetValue<string>() ?? string.Empty;
            await DelayAsync(context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, new JsonObject { ["embedding"] = ToArray(Embed(prompt)) }).ConfigureAwait(false);
        }

        private async Task OllamaBatchAsync(HttpContext context)
        {
            JsonNode? body = await ReadAsync(context).ConfigureAwait(false);
            JsonArray embeddings = new JsonArray();
            foreach (string input in Inputs(body?["input"])) embeddings.Add(ToArray(Embed(input)));
            await DelayAsync(context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, new JsonObject { ["embeddings"] = embeddings }).ConfigureAwait(false);
        }

        private async Task OpenAiAsync(HttpContext context)
        {
            JsonNode? body = await ReadAsync(context).ConfigureAwait(false);
            JsonArray data = new JsonArray();
            int index = 0;
            foreach (string input in Inputs(body?["input"]))
            {
                data.Add(new JsonObject { ["object"] = "embedding", ["index"] = index++, ["embedding"] = ToArray(Embed(input)) });
            }

            await DelayAsync(context.RequestAborted).ConfigureAwait(false);
            await WriteAsync(context, new JsonObject { ["object"] = "list", ["data"] = data, ["model"] = "stub" }).ConfigureAwait(false);
        }

        private async Task DelayAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _Requests);
            if (_LatencyMs > 0) await Task.Delay(_LatencyMs, token).ConfigureAwait(false);
        }

        private static async Task<JsonNode?> ReadAsync(HttpContext context)
        {
            return await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }

        private static async Task WriteAsync(HttpContext context, JsonObject payload)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(payload.ToJsonString(), context.RequestAborted).ConfigureAwait(false);
        }

        private static List<string> Inputs(JsonNode? input)
        {
            List<string> inputs = new List<string>();
            if (input is JsonArray array)
            {
                foreach (JsonNode? item in array) inputs.Add(item?.GetValue<string>() ?? string.Empty);
            }
            else if (input != null && input.GetValueKind() == JsonValueKind.String)
            {
                inputs.Add(input.GetValue<string>());
            }

            return inputs;
        }

        private static JsonArray ToArray(float[] vector)
        {
            JsonArray array = new JsonArray();
            foreach (float v in vector) array.Add(v);
            return array;
        }

        private static uint Fnv(string text, int start, int length)
        {
            uint hash = 2166136261u;
            for (int i = start; i < start + length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }

            return hash;
        }

        #endregion
    }
}
