namespace NotDory.Core.Recall
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using NotDory.Core.Enums;
    using NotDory.Core.Models;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Builds the PolyPrompt client for an inference endpoint, so every job (chat, query steps, reranking) talks to a
    /// model the same way: the endpoint's API format picks the client, and NotDory's generic authentication is applied by
    /// a per-endpoint handler over the shared transport (which retries transient failures).
    /// </summary>
    public static class ModelClientFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a completion (chat) client for an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="transport">The shared HTTP transport; it is not disposed with the client.</param>
        /// <param name="timeoutMs">The client's request timeout in milliseconds, at least 1.</param>
        /// <returns>The client; the caller disposes it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or transport is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when timeoutMs is below 1.</exception>
        /// <exception cref="NotSupportedException">Thrown when the endpoint's API format has no completion API (TEI).</exception>
        public static CompletionClientBase Create(ModelEndpoint endpoint, HttpMessageHandler transport, int timeoutMs)
        {
            Validate(endpoint, transport, timeoutMs);

            string baseUrl = endpoint.GetBaseUrl();
            CompletionClientBase client;
            if (endpoint.ApiFormat == ApiFormatEnum.Gemini)
            {
                // Gemini presents its credential through PolyPrompt's native Gemini key handling, so hand the secret to the
                // client directly rather than through the generic auth handler.
                HttpClient geminiTransport = new HttpClient(transport, false) { Timeout = Timeout.InfiniteTimeSpan };
                client = new GeminiCompletionClient(baseUrl, endpoint.AuthSecret ?? string.Empty, null, geminiTransport);
            }
            else
            {
                HttpClient authed = AuthedTransport(endpoint, transport);
                switch (endpoint.ApiFormat)
                {
                    case ApiFormatEnum.Ollama:
                        client = new OllamaCompletionClient(baseUrl, null, null, authed);
                        break;
                    case ApiFormatEnum.Cohere:
                        client = new CohereCompletionClient(baseUrl, null, null, authed);
                        break;
                    case ApiFormatEnum.Tei:
                        authed.Dispose();
                        throw new NotSupportedException("API format " + endpoint.ApiFormat + " has no completion API.");
                    default:
                        client = new OpenAiCompletionClient(baseUrl, null, null, authed);
                        break;
                }
            }

            if (!string.IsNullOrEmpty(endpoint.Model)) client.Model = endpoint.Model;
            client.TimeoutMs = timeoutMs;
            return client;
        }

        /// <summary>
        /// Create a cross-encoder rerank client for an endpoint whose API format is rerank-only (TEI or Cohere).
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="transport">The shared HTTP transport; it is not disposed with the client.</param>
        /// <param name="timeoutMs">The client's request timeout in milliseconds, at least 1.</param>
        /// <returns>The client; the caller disposes it.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint or transport is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when timeoutMs is below 1.</exception>
        /// <exception cref="NotSupportedException">Thrown when the endpoint's API format has no rerank API.</exception>
        public static RerankClientBase CreateRerank(ModelEndpoint endpoint, HttpMessageHandler transport, int timeoutMs)
        {
            Validate(endpoint, transport, timeoutMs);
            if (!ApiFormatCapabilities.IsRerankOnly(endpoint.ApiFormat))
                throw new NotSupportedException("API format " + endpoint.ApiFormat + " has no rerank API.");

            string baseUrl = endpoint.GetBaseUrl();
            HttpClient authed = AuthedTransport(endpoint, transport);
            RerankClientBase client = endpoint.ApiFormat == ApiFormatEnum.Tei
                ? new TeiRerankClient(baseUrl, null, null, authed)
                : new CohereRerankClient(baseUrl, null, null, authed);

            if (!string.IsNullOrEmpty(endpoint.Model)) client.Model = endpoint.Model;
            client.TimeoutMs = timeoutMs;
            return client;
        }

        /// <summary>
        /// The reasoning effort to send on chat calls to an endpoint, from its <see cref="ModelEndpoint.Reasoning"/>
        /// setting: null for Default (no reasoning field, the model decides), Minimal for Off (thinking off where the
        /// provider allows it), and the matching level otherwise.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The reasoning effort, or null to send none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static ReasoningEffort? ReasoningFor(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            switch (endpoint.Reasoning)
            {
                case ReasoningModeEnum.Off: return ReasoningEffort.Minimal;
                case ReasoningModeEnum.Low: return ReasoningEffort.Low;
                case ReasoningModeEnum.Medium: return ReasoningEffort.Medium;
                case ReasoningModeEnum.High: return ReasoningEffort.High;
                default: return null;
            }
        }

        #endregion

        #region Private-Methods

        private static void Validate(ModelEndpoint endpoint, HttpMessageHandler transport, int timeoutMs)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (timeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "The timeout must be at least 1 ms.");
        }

        private static HttpClient AuthedTransport(ModelEndpoint endpoint, HttpMessageHandler transport)
        {
            // NotDory's generic auth (bearer, header, query, basic, access-secret) through a per-endpoint delegating
            // handler; clients get a null key so PolyPrompt does not add its own Authorization header.
            return new HttpClient(new EndpointAuthHandler(endpoint, transport), false) { Timeout = Timeout.InfiniteTimeSpan };
        }

        #endregion
    }
}
