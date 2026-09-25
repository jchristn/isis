namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Observability;

    /// <summary>
    /// Calls a configured rerank endpoint to score how well each candidate passage answers a query. Supports the Hugging
    /// Face Text Embeddings Inference API (<see cref="ApiFormatEnum.Tei"/>) and the Cohere-compatible API
    /// (<see cref="ApiFormatEnum.Cohere"/>, also used for <see cref="ApiFormatEnum.VLlm"/>), both served by cross-encoders,
    /// and a chat model (<see cref="ApiFormatEnum.Ollama"/> or <see cref="ApiFormatEnum.OpenAI"/>) prompted to rate every
    /// passage in one call.
    /// </summary>
    public class RerankService
    {
        #region Private-Members

        private readonly HttpClient _HttpClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the rerank service.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to call endpoints.</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
        public RerankService(HttpClient httpClient)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Score each passage against the query.
        /// </summary>
        /// <param name="endpoint">The rerank endpoint to call.</param>
        /// <param name="query">The query text.</param>
        /// <param name="passages">The candidate passages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One score per passage, in the order the passages were given. Higher is more relevant; TEI and
        /// Cohere-compatible endpoints answer in the range 0 to 1.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the endpoint's API format has no rerank API.</exception>
        /// <exception cref="ModelEndpointUnavailableException">Thrown when the endpoint is still at capacity or unavailable after retries.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error or an unparseable response.</exception>
        public async Task<double[]> RerankAsync(ModelEndpoint endpoint, string query, IReadOnlyList<string> passages, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (passages == null) throw new ArgumentNullException(nameof(passages));
            if (passages.Count == 0) return new double[0];

            bool tei = endpoint.ApiFormat == ApiFormatEnum.Tei;
            bool chatOllama = endpoint.ApiFormat == ApiFormatEnum.Ollama;
            bool chatOpenAi = endpoint.ApiFormat == ApiFormatEnum.OpenAI;
            if (!tei && !chatOllama && !chatOpenAi && endpoint.ApiFormat != ApiFormatEnum.Cohere && endpoint.ApiFormat != ApiFormatEnum.VLlm)
            {
                throw new NotSupportedException("API format " + endpoint.ApiFormat + " has no rerank API; use Tei, Cohere, or a chat model (Ollama or OpenAI).");
            }

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            string path;
            object payload;
            if (tei)
            {
                path = "/rerank";
                payload = new { query = query, texts = passages, truncate = true, raw_scores = false };
            }
            else if (chatOllama)
            {
                path = "/api/chat";
                payload = new
                {
                    model = model,
                    stream = false,
                    format = "json",
                    options = new { temperature = 0 },
                    messages = new[] { new { role = "user", content = ChatPrompt(query, passages) } }
                };
            }
            else if (chatOpenAi)
            {
                path = "/v1/chat/completions";
                payload = new
                {
                    model = model,
                    temperature = 0,
                    messages = new[] { new { role = "user", content = ChatPrompt(query, passages) } }
                };
            }
            else
            {
                path = "/v1/rerank";
                payload = new { model = model, query = query, documents = passages, top_n = passages.Count };
            }

            string endpointHost = ResolveHost(endpoint.GetBaseUrl());
            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("rerank", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagEndpoint, endpointHost);
            activity?.SetTag(IsisTelemetry.TagModel, model);

            try
            {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 60000);

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint.GetBaseUrl() + path);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                EndpointAuthenticator.Apply(request, endpoint);

                HttpResponseMessage response = await _HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (TransientRetryHandler.IsTransient(response.StatusCode)) throw new ModelEndpointUnavailableException("Rerank endpoint is temporarily unavailable (" + (int)response.StatusCode + "): " + body, (int)response.StatusCode);
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Rerank endpoint returned " + (int)response.StatusCode + ": " + body);

                if (chatOllama || chatOpenAi) return ParseChatScores(body, chatOllama, passages.Count);
                return ParseScores(body, tei, passages.Count);
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(activity, e);
                throw;
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { IsisTelemetry.TagEndpoint, endpointHost }, { IsisTelemetry.TagModel, model }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.RerankDuration.Record(seconds, tags);
                IsisTelemetry.RerankRequests.Add(1, tags);
            }
        }

        #endregion

        #region Private-Methods

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

        private static double[] ParseChatScores(string json, bool ollama, int count)
        {
            // The chat reply is the model's text; it should be {"scores": [...]}, but tolerate a bare array or text around
            // the JSON. Scores are 0 to 10 and are returned as 0 to 1 so they are comparable with cross-encoder scores.
            string content;
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                content = ollama
                    ? root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty
                    : root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            }
            catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException || e is IndexOutOfRangeException)
            {
                throw new InvalidOperationException("Unable to read the rerank model's reply: " + e.Message);
            }

            int start = content.IndexOfAny(new[] { '{', '[' });
            int end = Math.Max(content.LastIndexOf('}'), content.LastIndexOf(']'));
            if (start < 0 || end <= start) throw new InvalidOperationException("The rerank model's reply contained no JSON scores.");

            try
            {
                using JsonDocument scores = JsonDocument.Parse(content.Substring(start, end - start + 1));
                JsonElement array = scores.RootElement;
                if (array.ValueKind == JsonValueKind.Object && !array.TryGetProperty("scores", out array)) throw new InvalidOperationException("The rerank model's reply had no 'scores' array.");
                if (array.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("The rerank model's 'scores' is not an array.");
                if (array.GetArrayLength() != count) throw new InvalidOperationException("The rerank model returned " + array.GetArrayLength() + " scores for " + count + " passages.");

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
                throw new InvalidOperationException("Unable to parse the rerank model's scores: " + e.Message);
            }
        }

        private static string ResolveHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return "unknown";
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return uri.Host;
            return "unknown";
        }

        private static double[] ParseScores(string json, bool tei, int count)
        {
            // Both APIs answer a list of (index, score) sorted by score, not by input order, and may omit passages
            // (Cohere's top_n). Map back to input order; an omitted passage scores 0.
            double[] scores = new double[count];
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement results;
                string scoreProperty;

                if (tei)
                {
                    results = root;
                    scoreProperty = "score";
                }
                else
                {
                    if (!root.TryGetProperty("results", out results)) throw new InvalidOperationException("Missing 'results' array.");
                    scoreProperty = "relevance_score";
                }

                if (results.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Expected an array of rerank results.");

                foreach (JsonElement item in results.EnumerateArray())
                {
                    if (!item.TryGetProperty("index", out JsonElement indexElement)) throw new InvalidOperationException("A rerank result is missing 'index'.");
                    if (!item.TryGetProperty(scoreProperty, out JsonElement scoreElement)) throw new InvalidOperationException("A rerank result is missing '" + scoreProperty + "'.");
                    int index = indexElement.GetInt32();
                    if (index < 0 || index >= count) throw new InvalidOperationException("A rerank result index " + index + " is out of range.");
                    scores[index] = scoreElement.GetDouble();
                }

                return scores;
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("Unable to parse rerank response: " + e.Message);
            }
        }

        #endregion
    }
}
