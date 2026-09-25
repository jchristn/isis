namespace Test.Benchmark.Runners
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// LLM-as-judge for chat answers, called directly against a model endpoint (never through Isis, so the judge is
    /// independent of the system under test). Uses LongMemEval-style yes/no grading against a gold answer.
    /// </summary>
    public class JudgeClient
    {
        #region Public-Members

        /// <summary>
        /// Description of the judge model.
        /// </summary>
        public string Description { get; }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly string _Format;
        private readonly string _BaseUrl;
        private readonly string _Model;
        private readonly string? _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="http">HTTP client.</param>
        /// <param name="format">Ollama or OpenAI (any OpenAI-compatible chat completions endpoint).</param>
        /// <param name="baseUrl">Base URL.</param>
        /// <param name="model">Model name.</param>
        /// <param name="apiKey">Optional bearer key.</param>
        public JudgeClient(HttpClient http, string format, string baseUrl, string model, string? apiKey)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _Format = format;
            _BaseUrl = baseUrl.TrimEnd('/');
            _Model = model;
            _ApiKey = apiKey;
            Description = format + " " + model + " @ " + _BaseUrl;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Grade an answer.
        /// </summary>
        /// <param name="question">The question.</param>
        /// <param name="gold">The gold answer, or "NOT_IN_MEMORY" for an unanswerable question.</param>
        /// <param name="response">The system's answer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the judge accepts the answer; null when the judge's reply could not be parsed.</returns>
        public async Task<bool?> GradeAsync(string question, string gold, string response, CancellationToken token)
        {
            string prompt;
            if (string.Equals(gold, "NOT_IN_MEMORY", StringComparison.Ordinal))
            {
                prompt = "I will give you a question and a response from an assistant that answers only from its memory. The memory does NOT contain the answer. "
                    + "Answer yes if the response says it does not know, cannot find it, or that the memories do not contain the answer. "
                    + "Answer no if the response asserts a specific answer.\n\n"
                    + "Question: " + question + "\n\nResponse: " + response + "\n\nDoes the response correctly decline to answer? Answer yes or no only.";
            }
            else
            {
                prompt = "I will give you a question, a correct answer, and a response from a model. Answer yes if the response contains the correct answer "
                    + "or is equivalent to it (paraphrases and extra detail are fine). Answer no if the response is wrong, contradicts the correct answer, "
                    + "only gives part of a multi-part answer, or says it does not know.\n\n"
                    + "Question: " + question + "\n\nCorrect answer: " + gold + "\n\nModel response: " + response
                    + "\n\nIs the model response correct? Answer yes or no only.";
            }

            string reply = await CompleteAsync(prompt, token).ConfigureAwait(false);
            string cleaned = StripThinking(reply).Trim().ToLowerInvariant();
            if (cleaned.StartsWith("yes", StringComparison.Ordinal)) return true;
            if (cleaned.StartsWith("no", StringComparison.Ordinal)) return false;
            if (Regex.IsMatch(cleaned, "\\byes\\b") && !Regex.IsMatch(cleaned, "\\bno\\b")) return true;
            if (Regex.IsMatch(cleaned, "\\bno\\b") && !Regex.IsMatch(cleaned, "\\byes\\b")) return false;
            return null;
        }

        /// <summary>
        /// Remove &lt;think&gt; blocks some reasoning models emit inline.
        /// </summary>
        /// <param name="text">Model output.</param>
        /// <returns>The text without thinking blocks.</returns>
        public static string StripThinking(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string stripped = Regex.Replace(text, "<think>.*?</think>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            int open = stripped.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            return open >= 0 ? stripped.Substring(0, open) : stripped;
        }

        #endregion

        #region Private-Methods

        private async Task<string> CompleteAsync(string prompt, CancellationToken token)
        {
            bool ollama = string.Equals(_Format, "Ollama", StringComparison.OrdinalIgnoreCase);
            JsonObject body;
            string path;
            if (ollama)
            {
                path = "/api/chat";
                body = new JsonObject
                {
                    ["model"] = _Model,
                    ["stream"] = false,
                    ["think"] = false,
                    ["options"] = new JsonObject { ["temperature"] = 0 },
                    ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } }
                };
            }
            else
            {
                path = "/v1/chat/completions";
                body = new JsonObject
                {
                    ["model"] = _Model,
                    ["temperature"] = 0,
                    ["max_tokens"] = 16,
                    ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } }
                };
            }

            // Shared judge endpoints refuse requests at capacity (429) or while no backend is healthy (502/503); retry
            // those with backoff instead of leaving the answer ungraded.
            string text = string.Empty;
            for (int attempt = 0; ; attempt++)
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _BaseUrl + path);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(_ApiKey)) request.Headers.Add("Authorization", "Bearer " + _ApiKey);
                using HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if ((status == 429 || status == 502 || status == 503) && attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt)), token).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Judge returned " + status + ": " + text);
                break;
            }

            JsonNode? parsed = JsonNode.Parse(text);
            if (ollama) return parsed?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
            return parsed?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
        }

        #endregion
    }
}
