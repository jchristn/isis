namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Observability;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Calls a configured inference endpoint to produce a completion, via PolyPrompt. Supports OpenAI-compatible,
    /// Ollama, and Gemini chat APIs — including token-by-token streaming with reasoning ("thinking") deltas and
    /// native per-turn statistics (time-to-first-token, token counts, tokens/sec). Used for memory hygiene and
    /// the chat-with-memory surface.
    /// </summary>
    public class InferenceService
    {
        #region Private-Members

        private readonly HttpMessageHandler _Transport;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the inference service.
        /// </summary>
        /// <param name="transport">The shared transport handler PolyPrompt calls through. Isis wraps it
        /// per-endpoint with an <see cref="EndpointAuthHandler"/> to apply the endpoint's configured auth
        /// (except for the Gemini format, whose key is presented via PolyPrompt's native query handling).
        /// The service never disposes this handler; the owner does.</param>
        /// <exception cref="ArgumentNullException">Thrown when transport is null.</exception>
        public InferenceService(HttpMessageHandler transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
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
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error.</exception>
        public async Task<string> CompleteAsync(ModelEndpoint endpoint, string systemPrompt, string userPrompt, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            string endpointHost = ResolveHost(endpoint.GetBaseUrl());
            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("inference", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagEndpoint, endpointHost);
            activity?.SetTag(IsisTelemetry.TagModel, model);
            activity?.SetTag(IsisTelemetry.TagStreaming, false);

            try
            {
                using CompletionClientBase client = CreateClient(endpoint);
                ChatResponse response = await client.ChatAsync(userPrompt, CreateOptions(systemPrompt), token).ConfigureAwait(false);
                if (!response.Success) throw new InvalidOperationException(response.Error ?? "Inference request failed.");
                return response.Text ?? string.Empty;
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
                TagList tags = new TagList { { IsisTelemetry.TagEndpoint, endpointHost }, { IsisTelemetry.TagModel, model }, { IsisTelemetry.TagStreaming, false }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.InferenceDuration.Record(seconds, tags);
                IsisTelemetry.InferenceRequests.Add(1, tags);
            }
        }

        /// <summary>
        /// Stream a completion token-by-token from an inference endpoint. Yields incremental content and
        /// reasoning ("thinking") slices as they arrive, and a terminal chunk carrying PolyPrompt's aggregate
        /// statistics (token counts, time-to-first-token, generation time, tokens/sec).
        /// </summary>
        /// <param name="endpoint">The inference endpoint to call.</param>
        /// <param name="systemPrompt">The system/grounding prompt.</param>
        /// <param name="userPrompt">The user prompt.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async stream of inference chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an error.</exception>
        public async IAsyncEnumerable<InferenceChunk> CompleteStreamingAsync(ModelEndpoint endpoint, string systemPrompt, string userPrompt, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));

            string endpointHost = ResolveHost(endpoint.GetBaseUrl());
            string model = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "error";
            bool ttfbRecorded = false;
            TagList idTags = new TagList { { IsisTelemetry.TagEndpoint, endpointHost }, { IsisTelemetry.TagModel, model } };

            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("inference", ActivityKind.Client);
            activity?.SetTag(IsisTelemetry.TagEndpoint, endpointHost);
            activity?.SetTag(IsisTelemetry.TagModel, model);
            activity?.SetTag(IsisTelemetry.TagStreaming, true);

            try
            {
                using CompletionClientBase client = CreateClient(endpoint);
                ChatStreamingResponse response = await client.ChatStreamingAsync(userPrompt, CreateOptions(systemPrompt), token).ConfigureAwait(false);
                if (!response.Success) throw new InvalidOperationException(response.Error ?? "Streaming inference request failed.");

                int promptTokens = 0;
                int completionTokens = 0;

                if (response.Chunks != null)
                {
                    await foreach (ChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
                    {
                        if (chunk.Usage != null)
                        {
                            if (chunk.Usage.PromptTokens > 0) promptTokens = chunk.Usage.PromptTokens.Value;
                            if (chunk.Usage.CompletionTokens > 0) completionTokens = chunk.Usage.CompletionTokens.Value;
                        }

                        if (!string.IsNullOrEmpty(chunk.Text) || !string.IsNullOrEmpty(chunk.ReasoningText))
                        {
                            if (!ttfbRecorded && !string.IsNullOrEmpty(chunk.Text))
                            {
                                IsisTelemetry.InferenceTtfbDuration.Record(Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds, idTags);
                                ttfbRecorded = true;
                            }

                            IsisTelemetry.InferenceStreamChunks.Add(1, idTags);
                            yield return new InferenceChunk { Content = chunk.Text, Reasoning = chunk.ReasoningText };
                        }
                    }
                }

                if (response.Usage != null)
                {
                    if (response.Usage.PromptTokens > 0) promptTokens = response.Usage.PromptTokens.Value;
                    if (response.Usage.CompletionTokens > 0) completionTokens = response.Usage.CompletionTokens.Value;
                }

                double ttft = response.TimeToFirstTokenMs;
                double lastToken = response.TimeToLastTokenMs;
                double generation = lastToken > ttft ? lastToken - ttft : Math.Max(0.0, response.OverallRuntimeMs - ttft);

                yield return new InferenceChunk
                {
                    Done = true,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TimeToFirstTokenMs = ttft,
                    GenerationMs = generation,
                    TokensPerSecond = response.OverallTokensPerSecond
                };

                telemetryOutcome = "success";
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { IsisTelemetry.TagEndpoint, endpointHost }, { IsisTelemetry.TagModel, model }, { IsisTelemetry.TagStreaming, true }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.InferenceDuration.Record(seconds, tags);
                IsisTelemetry.InferenceRequests.Add(1, tags);
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

        private CompletionClientBase CreateClient(ModelEndpoint endpoint)
        {
            string baseUrl = endpoint.GetBaseUrl();

            CompletionClientBase client;
            if (endpoint.ApiFormat == ApiFormatEnum.Gemini)
            {
                // Gemini presents its credential via PolyPrompt's native "?key=" query handling, so hand the
                // secret to the client directly rather than through the generic auth handler.
                HttpClient transport = new HttpClient(_Transport, false) { Timeout = Timeout.InfiniteTimeSpan };
                client = new GeminiClient(baseUrl, endpoint.AuthSecret ?? string.Empty, null, transport);
            }
            else
            {
                // All other formats: apply Isis's generic auth (bearer / header / query / basic / access-secret)
                // via a per-endpoint delegating handler over the shared transport, and pass a null key so
                // PolyPrompt does not also add its own Authorization header.
                HttpClient transport = new HttpClient(new EndpointAuthHandler(endpoint, _Transport), false) { Timeout = Timeout.InfiniteTimeSpan };
                client = endpoint.ApiFormat == ApiFormatEnum.Ollama
                    ? new OllamaClient(baseUrl, null, null, transport)
                    : new OpenAiClient(baseUrl, null, null, transport);
            }

            if (!string.IsNullOrEmpty(endpoint.Model)) client.Model = endpoint.Model;
            // Streaming a reasoning model can take a while before and between tokens; give the client a
            // generous budget so PolyPrompt does not cancel the stream. Actual cancellation flows through the
            // request's CancellationToken.
            client.TimeoutMs = Math.Max(endpoint.TimeoutMs, 600000);
            return client;
        }

        private static ChatCompletionOptions CreateOptions(string systemPrompt)
        {
            ChatCompletionOptions options = new ChatCompletionOptions();
            if (!string.IsNullOrEmpty(systemPrompt)) options.SystemPrompt = systemPrompt;
            return options;
        }

        #endregion
    }
}
