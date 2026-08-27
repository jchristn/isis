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
    /// Calls a configured inference endpoint to produce a completion. Supports OpenAI-compatible and Ollama
    /// chat APIs. Used for memory hygiene and the chat-with-memory surface.
    /// </summary>
    public class InferenceService
    {
        #region Private-Members

        private readonly HttpClient _HttpClient;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the inference service.
        /// </summary>
        /// <param name="httpClient">The HTTP client used to call endpoints.</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
        public InferenceService(HttpClient httpClient)
        {
            _HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Produce a completion from a system and user prompt.
        /// </summary>
        /// <param name="endpoint">The inference endpoint to call.</param>
        /// <param name="systemPrompt">The system/grounding prompt.</param>
        /// <param name="userPrompt">The user prompt.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completion text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error or an unparseable response.</exception>
        public async Task<string> CompleteAsync(ModelEndpoint endpoint, string systemPrompt, string userPrompt, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(systemPrompt)) messages.Add(new Dictionary<string, string> { ["role"] = "system", ["content"] = systemPrompt });
            messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = userPrompt });

            bool ollama = endpoint.ApiFormat == ApiFormatEnum.Ollama;
            string path = ollama ? "/api/chat" : "/v1/chat/completions";
            object payload = new { model = model, messages = messages, stream = false };

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
            string text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Inference endpoint returned " + (int)response.StatusCode + ": " + text);

            return ParseContent(text, ollama);
        }

        #endregion

        #region Private-Methods

        private static string ParseContent(string json, bool ollama)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (ollama)
                {
                    if (root.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement ollamaContent))
                    {
                        return ollamaContent.GetString() ?? string.Empty;
                    }
                }
                else
                {
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        JsonElement first = choices[0];
                        if (first.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content))
                        {
                            return content.GetString() ?? string.Empty;
                        }
                    }
                }
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException("Unable to parse inference response: " + e.Message);
            }

            throw new InvalidOperationException("Inference response did not contain a completion.");
        }

        #endregion
    }
}
