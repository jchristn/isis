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
    /// Calls a configured rerank endpoint (a cross-encoder) to score how well each candidate passage answers a query.
    /// Supports the Hugging Face Text Embeddings Inference API (<see cref="ApiFormatEnum.Tei"/>) and the
    /// Cohere-compatible API (<see cref="ApiFormatEnum.Cohere"/>, also used for <see cref="ApiFormatEnum.VLlm"/>).
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
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error or an unparseable response.</exception>
        public async Task<double[]> RerankAsync(ModelEndpoint endpoint, string query, IReadOnlyList<string> passages, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (passages == null) throw new ArgumentNullException(nameof(passages));
            if (passages.Count == 0) return new double[0];

            bool tei = endpoint.ApiFormat == ApiFormatEnum.Tei;
            if (!tei && endpoint.ApiFormat != ApiFormatEnum.Cohere && endpoint.ApiFormat != ApiFormatEnum.VLlm)
            {
                throw new NotSupportedException("API format " + endpoint.ApiFormat + " has no rerank API; use Tei or Cohere.");
            }

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            string path = tei ? "/rerank" : "/v1/rerank";
            object payload = tei
                ? new { query = query, texts = passages, truncate = true, raw_scores = false }
                : (object)new { model = model, query = query, documents = passages, top_n = passages.Count };

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
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Rerank endpoint returned " + (int)response.StatusCode + ": " + body);

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
