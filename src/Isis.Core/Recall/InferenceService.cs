namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
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

        /// <summary>
        /// Stream a completion token-by-token from an inference endpoint. Yields incremental content and
        /// reasoning ("thinking") slices as they arrive, and token counts on the terminal chunk when the
        /// endpoint reports them. Supports Ollama (newline-delimited JSON) and OpenAI-compatible (SSE) streams.
        /// </summary>
        /// <param name="endpoint">The inference endpoint to call.</param>
        /// <param name="systemPrompt">The system/grounding prompt.</param>
        /// <param name="userPrompt">The user prompt.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async stream of inference chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error status.</exception>
        public async IAsyncEnumerable<InferenceChunk> CompleteStreamingAsync(ModelEndpoint endpoint, string systemPrompt, string userPrompt, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            List<Dictionary<string, string>> messages = new List<Dictionary<string, string>>();
            if (!string.IsNullOrEmpty(systemPrompt)) messages.Add(new Dictionary<string, string> { ["role"] = "system", ["content"] = systemPrompt });
            messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = userPrompt });

            bool ollama = endpoint.ApiFormat == ApiFormatEnum.Ollama;
            string path = ollama ? "/api/chat" : "/v1/chat/completions";
            object payload = ollama
                ? new { model = model, messages = messages, stream = true }
                : (object)new { model = model, messages = messages, stream = true, stream_options = new { include_usage = true } };

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(endpoint.TimeoutMs > 0 ? endpoint.TimeoutMs : 120000);

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint.GetBaseUrl() + path);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(endpoint.ApiKey))
            {
                if (endpoint.ApiFormat == ApiFormatEnum.Gemini) request.Headers.TryAddWithoutValidation("x-goog-api-key", endpoint.ApiKey);
                else request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.ApiKey);
            }

            using HttpResponseMessage response = await _HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errorText = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                throw new InvalidOperationException("Inference endpoint returned " + (int)response.StatusCode + ": " + errorText);
            }

            using Stream responseStream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);

            while (true)
            {
                string? line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                if (line == null) break;

                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("data:", StringComparison.Ordinal)) trimmed = trimmed.Substring("data:".Length).Trim();
                if (trimmed == "[DONE]") break;
                if (trimmed.Length == 0 || trimmed[0] != '{') continue;

                InferenceChunk? chunk = ParseChunk(trimmed, ollama);
                if (chunk != null) yield return chunk;
            }
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

        private static InferenceChunk? ParseChunk(string json, bool ollama)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                InferenceChunk chunk = new InferenceChunk();

                if (ollama)
                {
                    if (root.TryGetProperty("message", out JsonElement message))
                    {
                        if (message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) chunk.Content = content.GetString();
                        if (message.TryGetProperty("thinking", out JsonElement thinking) && thinking.ValueKind == JsonValueKind.String) chunk.Reasoning = thinking.GetString();
                    }

                    if (root.TryGetProperty("done", out JsonElement done) && done.ValueKind == JsonValueKind.True) chunk.Done = true;
                    if (root.TryGetProperty("prompt_eval_count", out JsonElement promptEval) && promptEval.TryGetInt32(out int promptEvalValue)) chunk.PromptTokens = promptEvalValue;
                    if (root.TryGetProperty("eval_count", out JsonElement evalCount) && evalCount.TryGetInt32(out int evalCountValue)) chunk.CompletionTokens = evalCountValue;
                }
                else
                {
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        JsonElement first = choices[0];
                        if (first.TryGetProperty("delta", out JsonElement delta))
                        {
                            if (delta.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String) chunk.Content = content.GetString();
                            if (delta.TryGetProperty("reasoning_content", out JsonElement reasoning) && reasoning.ValueKind == JsonValueKind.String) chunk.Reasoning = reasoning.GetString();
                            else if (delta.TryGetProperty("reasoning", out JsonElement reasoningAlt) && reasoningAlt.ValueKind == JsonValueKind.String) chunk.Reasoning = reasoningAlt.GetString();
                        }

                        if (first.TryGetProperty("finish_reason", out JsonElement finish) && finish.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(finish.GetString())) chunk.Done = true;
                    }

                    if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        if (usage.TryGetProperty("prompt_tokens", out JsonElement promptTokens) && promptTokens.TryGetInt32(out int promptTokensValue)) chunk.PromptTokens = promptTokensValue;
                        if (usage.TryGetProperty("completion_tokens", out JsonElement completionTokens) && completionTokens.TryGetInt32(out int completionTokensValue)) chunk.CompletionTokens = completionTokensValue;
                    }
                }

                return chunk;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
