namespace NotDory.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using NotDory.Core.Enums;
    using NotDory.Core.Models;
    using NotDory.Core.Observability;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;

    /// <summary>
    /// Scores how well each candidate passage answers a query, through PolyPrompt like every other model call. A
    /// cross-encoder (<see cref="ApiFormatEnum.Tei"/> or <see cref="ApiFormatEnum.Cohere"/>) scores the passages
    /// through its rerank API; a chat model (<see cref="ApiFormatEnum.Ollama"/>, <see cref="ApiFormatEnum.OpenAI"/>,
    /// <see cref="ApiFormatEnum.VLlm"/>, or <see cref="ApiFormatEnum.Gemini"/>) is prompted to rate every passage in one
    /// call. Scores are 0 to 1 either way.
    /// </summary>
    public class RerankService
    {
        #region Private-Members

        private readonly HttpMessageHandler _Transport;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the rerank service.
        /// </summary>
        /// <param name="transport">The shared HTTP transport (typically a retrying handler); not disposed by the service.</param>
        /// <exception cref="ArgumentNullException">Thrown when transport is null.</exception>
        public RerankService(HttpMessageHandler transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Score each passage against the query.
        /// </summary>
        /// <param name="endpoint">The inference endpoint that reranks.</param>
        /// <param name="query">The query text.</param>
        /// <param name="passages">The candidate passages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One score per passage, in the order the passages were given, from 0 to 1 (higher is more relevant).
        /// A passage a cross-encoder did not score gets 0.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the endpoint's API format cannot rerank.</exception>
        /// <exception cref="ModelEndpointUnavailableException">Thrown when the endpoint is still at capacity or unavailable after retries.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error.</exception>
        /// <exception cref="RerankReplyException">Thrown when a chat model's reply holds no usable scores.</exception>
        public async Task<double[]> RerankAsync(ModelEndpoint endpoint, string query, IReadOnlyList<string> passages, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (passages == null) throw new ArgumentNullException(nameof(passages));
            if (passages.Count == 0) return new double[0];
            if (!ApiFormatCapabilities.CanRerank(endpoint.ApiFormat))
                throw new NotSupportedException("API format " + endpoint.ApiFormat + " cannot rerank.");

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            string endpointHost = ResolveHost(endpoint.GetBaseUrl());
            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = NotDoryTelemetry.ActivitySource.StartActivity("rerank", ActivityKind.Client);
            activity?.SetTag(NotDoryTelemetry.TagEndpoint, endpointHost);
            activity?.SetTag(NotDoryTelemetry.TagModel, model);

            try
            {
                // The endpoint's timeout bounds the whole call, so a slow reranker falls back to retrieval order rather
                // than holding up the search.
                int timeoutMs = endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 60000;
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeoutMs);
                if (ApiFormatCapabilities.IsRerankOnly(endpoint.ApiFormat))
                {
                    using RerankClientBase reranker = ModelClientFactory.CreateRerank(endpoint, _Transport, timeoutMs);
                    return await CrossEncoderAsync(reranker, endpoint, query, passages, cts.Token).ConfigureAwait(false);
                }

                using CompletionClientBase client = ModelClientFactory.Create(endpoint, _Transport, timeoutMs);
                return await PromptedAsync(client, endpoint, query, passages, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                NotDoryTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { NotDoryTelemetry.TagEndpoint, endpointHost }, { NotDoryTelemetry.TagModel, model }, { NotDoryTelemetry.TagOutcome, telemetryOutcome } };
                NotDoryTelemetry.RerankDuration.Record(seconds, tags);
                NotDoryTelemetry.RerankRequests.Add(1, tags);
            }
        }

        /// <summary>
        /// Read the scores out of a chat model's rating reply. The reply should be {"scores": [...]} with ratings from 0
        /// to 10; a bare array or text around the JSON is tolerated. Ratings are returned as 0 to 1.
        /// </summary>
        /// <param name="reply">The model's reply text.</param>
        /// <param name="count">The number of passages rated.</param>
        /// <returns>One score per passage, from 0 to 1.</returns>
        /// <exception cref="RerankReplyException">Thrown when the reply holds no usable scores or the wrong number.</exception>
        public static double[] ParseRatings(string? reply, int count)
        {
            string content = reply ?? string.Empty;
            int start = content.IndexOfAny(new[] { '{', '[' });
            int end = Math.Max(content.LastIndexOf('}'), content.LastIndexOf(']'));
            if (start < 0 || end <= start) throw new RerankReplyException("The rerank model's reply contained no JSON scores.");

            try
            {
                using JsonDocument scores = JsonDocument.Parse(content.Substring(start, end - start + 1));
                JsonElement array = scores.RootElement;
                if (array.ValueKind == JsonValueKind.Object && !array.TryGetProperty("scores", out array)) throw new RerankReplyException("The rerank model's reply had no 'scores' array.");
                if (array.ValueKind != JsonValueKind.Array) throw new RerankReplyException("The rerank model's 'scores' is not an array.");
                if (array.GetArrayLength() != count) throw new RerankReplyException("The rerank model returned " + array.GetArrayLength() + " scores for " + count + " passages.");

                double[] result = new double[count];
                int i = 0;
                foreach (JsonElement item in array.EnumerateArray())
                {
                    double value = item.ValueKind == JsonValueKind.Number ? item.GetDouble() : 0.0;
                    result[i++] = Math.Max(0.0, Math.Min(10.0, value)) / 10.0;
                }

                return result;
            }
            catch (JsonException e)
            {
                throw new RerankReplyException("Unable to parse the rerank model's scores: " + e.Message);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<double[]> CrossEncoderAsync(RerankClientBase client, ModelEndpoint endpoint, string query, IReadOnlyList<string> passages, CancellationToken token)
        {
            // TEI returns sigmoid scores in 0..1 when raw scores are off, and truncates passages longer than the model's
            // window; Cohere-compatible APIs answer relevance scores in 0..1.
            RerankOptions options = endpoint.ApiFormat == ApiFormatEnum.Tei
                ? new TeiRerankOptions { RawScores = false, Truncate = true }
                : new RerankOptions();
            if (!string.IsNullOrEmpty(endpoint.Model)) options.Model = endpoint.Model;

            RerankResponse response = await client.RerankAsync(query, new List<string>(passages), options, token).ConfigureAwait(false);
            if (!response.Success) ThrowFor(response.StatusCode ?? 0, response.Error);

            double[] scores = new double[passages.Count];
            foreach (RerankResult result in response.Results)
            {
                if (result.Index >= 0 && result.Index < scores.Length) scores[result.Index] = result.Score;
            }

            return scores;
        }

        private static async Task<double[]> PromptedAsync(CompletionClientBase client, ModelEndpoint endpoint, string query, IReadOnlyList<string> passages, CancellationToken token)
        {
            CompletionOptions options = new CompletionOptions { Temperature = 0, ReasoningEffort = ModelClientFactory.ReasoningFor(endpoint) };
            ChatResponse response = await client.ChatAsync(ChatPrompt(query, passages), options, token).ConfigureAwait(false);
            if (!response.Success) ThrowFor(response.StatusCode ?? 0, response.Error);
            return ParseRatings(response.Text, passages.Count);
        }

        private static void ThrowFor(int statusCode, string? error)
        {
            string detail = string.IsNullOrEmpty(error) ? "no detail" : error!;
            if (TransientRetryHandler.IsTransient((HttpStatusCode)statusCode))
                throw new ModelEndpointUnavailableException("Rerank endpoint is temporarily unavailable (" + statusCode + "): " + detail, statusCode);
            throw new InvalidOperationException("Rerank endpoint returned " + statusCode + ": " + detail);
        }

        private static string ChatPrompt(string query, IReadOnlyList<string> passages)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Rate how well each numbered passage answers the query, from 0 (irrelevant) to 10 (answers it directly). ");
            sb.Append("Judge only relevance to the query. Reply with JSON only, in the form {\"scores\": [s1, s2, ...]}, with exactly ");
            sb.Append(passages.Count).Append(" numbers in passage order.\n\nQuery: ").Append(query).Append("\n\n");
            for (int i = 0; i < passages.Count; i++)
            {
                sb.Append("Passage ").Append(i + 1).Append(":\n").Append(passages[i]).Append("\n\n");
            }

            return sb.ToString();
        }

        private static string ResolveHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return "unknown";
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return uri.Host;
            return "unknown";
        }

        #endregion
    }
}
