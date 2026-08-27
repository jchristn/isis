namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Calls a configured embedding endpoint to turn text into a vector. Supports OpenAI-compatible and
    /// Ollama embedding APIs. The resulting vector is supplied to RecallDB (bring-your-own-vector).
    /// </summary>
    public class EmbeddingService
    {
        #region Private-Members

        private readonly HttpClient _HttpClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the embedding service.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to call endpoints.</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
        public EmbeddingService(HttpClient httpClient)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Produce an embedding vector for the given text.
        /// </summary>
        /// <param name="endpoint">The embedding endpoint to call.</param>
        /// <param name="text">The text to embed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The embedding vector.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or text is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error or an unparseable response.</exception>
        public async Task<float[]> EmbedAsync(ModelEndpoint endpoint, string text, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (text == null) throw new ArgumentNullException(nameof(text));

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            bool ollama = endpoint.ApiFormat == ApiFormatEnum.Ollama;
            string path = ollama ? "/api/embeddings" : "/v1/embeddings";
            object payload = ollama ? new { model = model, prompt = text } : (object)new { model = model, input = text };

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 60000);

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint.GetBaseUrl() + path);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(endpoint.ApiKey))
            {
                if (endpoint.ApiFormat == ApiFormatEnum.Gemini) request.Headers.TryAddWithoutValidation("x-goog-api-key", endpoint.ApiKey);
                else request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.ApiKey);
            }

            HttpResponseMessage response = await _HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Embedding endpoint returned " + (int)response.StatusCode + ": " + body);

            return ParseVector(body, ollama);
        }

        #endregion

        #region Private-Methods

        private static float[] ParseVector(string json, bool ollama)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement array;

                if (ollama)
                {
                    if (!root.TryGetProperty("embedding", out array)) throw new InvalidOperationException("Missing 'embedding' array.");
                }
                else
                {
                    if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) throw new InvalidOperationException("Missing 'data' array.");
                    if (!data[0].TryGetProperty("embedding", out array)) throw new InvalidOperationException("Missing 'embedding' array.");
                }

                List<float> vector = new List<float>(array.GetArrayLength());
                foreach (JsonElement element in array.EnumerateArray())
                {
                    vector.Add((float)element.GetDouble());
                }

                return vector.ToArray();
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("Unable to parse embedding response: " + e.Message);
            }
        }

        #endregion
    }
}
