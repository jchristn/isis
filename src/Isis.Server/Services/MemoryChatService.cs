namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Observability;
    using Isis.Core.Recall;
    using Isis.Core.Stores;

    /// <summary>
    /// The chat-with-memory surface. Retrieves the most relevant memories from a scope and asks the
    /// configured inference endpoint to answer the user's question grounded in them, returning the answer
    /// with citations.
    /// </summary>
    public class MemoryChatService
    {
        #region Private-Members

        private readonly MemoryService _MemoryService;
        private readonly InferenceService _InferenceService;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the chat service.
        /// </summary>
        /// <param name="memoryService">The memory service used for retrieval.</param>
        /// <param name="inferenceService">The inference service used for answer synthesis.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MemoryChatService(MemoryService memoryService, InferenceService inferenceService)
        {
            _MemoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
            _InferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Answer a natural-language question about a scope's memory.
        /// </summary>
        /// <param name="scope">The scope to draw memory from.</param>
        /// <param name="inferenceEndpoint">The inference endpoint used to synthesize the answer.</param>
        /// <param name="question">The user's question.</param>
        /// <param name="topK">The maximum number of memories to retrieve.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer with citations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<ChatAnswer> AskAsync(Scope scope, ModelEndpoint inferenceEndpoint, string question, int topK, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (inferenceEndpoint == null) throw new ArgumentNullException(nameof(inferenceEndpoint));
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("A question is required.", nameof(question));

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? activity = IsisTelemetry.ActivitySource.StartActivity("chat ask", ActivityKind.Internal);
            activity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            activity?.SetTag(IsisTelemetry.TagStreaming, false);

            try
            {
                ContextResult ctx = await BuildContextAsync(scope, question, topK, token).ConfigureAwait(false);
                IsisTelemetry.ChatContextMemories.Record(ctx.Count, new TagList { { IsisTelemetry.TagStreaming, false } });

                ChatAnswer answer = new ChatAnswer();
                answer.RetrievalMode = ctx.Mode;
                answer.Notice = ctx.Notice;
                answer.Citations.AddRange(ctx.Citations);

                string userPrompt = BuildUserPrompt(question, ctx.ContextText);
                answer.Answer = await _InferenceService.CompleteAsync(inferenceEndpoint, _SystemPrompt, userPrompt, token).ConfigureAwait(false);
                return answer;
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
                TagList tags = new TagList { { IsisTelemetry.TagStreaming, false }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.ChatAskDuration.Record(seconds, tags);
                IsisTelemetry.ChatAsks.Add(1, tags);
            }
        }

        /// <summary>
        /// Answer a question about a scope's memory as a live stream, emitting a retrieval event, incremental
        /// thinking and answer deltas, and a final completion with citations and per-turn statistics. The
        /// <paramref name="emit"/> callback is invoked for each event (the caller frames it onto the wire).
        /// </summary>
        /// <param name="scope">The scope to draw memory from.</param>
        /// <param name="inferenceEndpoint">The inference endpoint used to synthesize the answer.</param>
        /// <param name="question">The user's question.</param>
        /// <param name="topK">The maximum number of memories to retrieve.</param>
        /// <param name="emit">Callback invoked with each event object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task AskStreamingAsync(Scope scope, ModelEndpoint inferenceEndpoint, string question, int topK, Func<object, CancellationToken, Task> emit, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (inferenceEndpoint == null) throw new ArgumentNullException(nameof(inferenceEndpoint));
            if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("A question is required.", nameof(question));
            if (emit == null) throw new ArgumentNullException(nameof(emit));

            long telemetryStart = Stopwatch.GetTimestamp();
            string telemetryOutcome = "success";
            using Activity? chatActivity = IsisTelemetry.ActivitySource.StartActivity("chat ask", ActivityKind.Internal);
            chatActivity?.SetTag(IsisTelemetry.TagScope, scope.Id);
            chatActivity?.SetTag(IsisTelemetry.TagStreaming, true);

            try
            {
            ContextResult ctx = await BuildContextAsync(scope, question, topK, token).ConfigureAwait(false);
            IsisTelemetry.ChatContextMemories.Record(ctx.Count, new TagList { { IsisTelemetry.TagStreaming, true } });

            List<ChatCitation> citations = ctx.Citations;
            string? notice = ctx.Notice;

            await emit(new { type = "retrieval", mode = ctx.ModeLabel, hits = ctx.HitPayloads, notice = notice }, token).ConfigureAwait(false);

            string systemPrompt = _SystemPrompt;
            string userPrompt = BuildUserPrompt(question, ctx.ContextText);

            StringBuilder answerBuilder = new StringBuilder();
            Stopwatch stopwatch = Stopwatch.StartNew();
            double ttftMs = 0.0;
            bool firstAnswer = true;
            int promptTokens = 0;
            int completionTokens = 0;
            double? providerTtft = null;
            double? providerGeneration = null;
            double? providerTps = null;
            bool inThink = false;
            string tail = string.Empty;

            async Task EmitSegmentAsync(bool thinking, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (thinking)
                {
                    await emit(new { type = "thinking", text = text }, token).ConfigureAwait(false);
                }
                else
                {
                    if (firstAnswer)
                    {
                        ttftMs = stopwatch.Elapsed.TotalMilliseconds;
                        firstAnswer = false;
                    }

                    answerBuilder.Append(text);
                    await emit(new { type = "delta", text = text }, token).ConfigureAwait(false);
                }
            }

            async Task ProcessContentAsync(string incoming)
            {
                string buffer = tail + incoming;
                tail = string.Empty;
                int pos = 0;
                while (pos < buffer.Length)
                {
                    string marker = inThink ? "</think>" : "<think>";
                    int idx = buffer.IndexOf(marker, pos, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                    {
                        int safe = SafeEmitBoundary(buffer, marker);
                        if (safe < pos) safe = pos;
                        await EmitSegmentAsync(inThink, buffer.Substring(pos, safe - pos)).ConfigureAwait(false);
                        tail = buffer.Substring(safe);
                        return;
                    }

                    await EmitSegmentAsync(inThink, buffer.Substring(pos, idx - pos)).ConfigureAwait(false);
                    inThink = !inThink;
                    pos = idx + marker.Length;
                }
            }

            await foreach (InferenceChunk chunk in _InferenceService.CompleteStreamingAsync(inferenceEndpoint, systemPrompt, userPrompt, token).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.Reasoning)) await EmitSegmentAsync(true, chunk.Reasoning!).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(chunk.Content)) await ProcessContentAsync(chunk.Content!).ConfigureAwait(false);
                if (chunk.PromptTokens.HasValue) promptTokens = chunk.PromptTokens.Value;
                if (chunk.CompletionTokens.HasValue) completionTokens = chunk.CompletionTokens.Value;
                if (chunk.TimeToFirstTokenMs.HasValue) providerTtft = chunk.TimeToFirstTokenMs.Value;
                if (chunk.GenerationMs.HasValue) providerGeneration = chunk.GenerationMs.Value;
                if (chunk.TokensPerSecond.HasValue) providerTps = chunk.TokensPerSecond.Value;
            }

            if (!string.IsNullOrEmpty(tail)) await EmitSegmentAsync(inThink, tail).ConfigureAwait(false);

            stopwatch.Stop();
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            double measuredGeneration = Math.Max(0.0, totalMs - ttftMs);
            double measuredTps = completionTokens > 0 && measuredGeneration > 0.0 ? completionTokens / (measuredGeneration / 1000.0) : 0.0;

            // Prefer the endpoint's own timing/throughput (reported by PolyPrompt) and fall back to the
            // wall-clock measurements when the endpoint does not report them.
            double timeToFirstTokenMs = providerTtft.HasValue && providerTtft.Value > 0.0 ? providerTtft.Value : ttftMs;
            double generationMs = providerGeneration.HasValue && providerGeneration.Value > 0.0 ? providerGeneration.Value : measuredGeneration;
            double tokensPerSecond = providerTps.HasValue && providerTps.Value > 0.0 ? providerTps.Value : measuredTps;

            await emit(new
            {
                type = "complete",
                answer = answerBuilder.ToString(),
                citations = citations,
                retrievalMode = ctx.ModeLabel,
                notice = notice,
                model = inferenceEndpoint.Model,
                promptTokens = promptTokens,
                completionTokens = completionTokens,
                totalTokens = promptTokens + completionTokens,
                timeToFirstTokenMs = timeToFirstTokenMs,
                generationMs = generationMs,
                tokensPerSecond = tokensPerSecond
            }, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                telemetryOutcome = "error";
                IsisTelemetry.RecordException(chatActivity, e);
                throw;
            }
            finally
            {
                double seconds = Stopwatch.GetElapsedTime(telemetryStart).TotalSeconds;
                TagList tags = new TagList { { IsisTelemetry.TagStreaming, true }, { IsisTelemetry.TagOutcome, telemetryOutcome } };
                IsisTelemetry.ChatAskDuration.Record(seconds, tags);
                IsisTelemetry.ChatAsks.Add(1, tags);
            }
        }

        #endregion

        #region Private-Methods

        private const string _SystemPrompt =
            "You are a memory assistant for a specific memory scope. Answer the user's question using only the provided memories. " +
            "Cite the memories you use by their slug in square brackets. " +
            "If the user asks what memories exist, or asks for an overview or summary, summarize the provided memories — do NOT claim you have none when memories are listed below. " +
            "Only if the memories genuinely do not contain the answer should you say so plainly.";

        private static string BuildUserPrompt(string question, string contextText)
        {
            return "Question: " + question + "\n\nMemories:\n" + (string.IsNullOrEmpty(contextText) ? "(none)" : contextText);
        }

        /// <summary>
        /// Build the grounding context for a question: run relevance search, and when it matches nothing
        /// (a broad "what memories do you have?" question, or a keyword-only store that cannot match the
        /// phrasing) fall back to enumerating the scope's memories so the model can summarize them rather
        /// than being handed "(none)".
        /// </summary>
        private async Task<ContextResult> BuildContextAsync(Scope scope, string question, int topK, CancellationToken token)
        {
            int k = topK < 1 ? 5 : topK;
            MemorySearchQuery query = new MemorySearchQuery { QueryText = question, Mode = SearchModeEnum.Hybrid, TopK = k };
            MemorySearchResult retrieval = await _MemoryService.SearchAsync(scope, query, token).ConfigureAwait(false);

            ContextResult ctx = new ContextResult { Mode = retrieval.EffectiveMode, ModeLabel = retrieval.EffectiveMode.ToString(), Notice = retrieval.Notice };
            StringBuilder sb = new StringBuilder();

            foreach (MemorySearchHit hit in retrieval.Hits)
            {
                AppendContextItem(sb, ctx, hit.Slug, hit.Title, hit.Snippet, hit.Score);
            }

            if (retrieval.Hits.Count == 0)
            {
                int cap = Math.Max(k, 25);
                List<Memory> all = await _MemoryService.EnumerateAsync(scope, null, cap, token).ConfigureAwait(false);
                foreach (Memory memory in all)
                {
                    string snippet = !string.IsNullOrEmpty(memory.Summary) ? memory.Summary! : Truncate(memory.Body, 600);
                    AppendContextItem(sb, ctx, memory.Slug, memory.Title, snippet, null);
                }

                ctx.Notice = all.Count > 0
                    ? "No memory directly matched the question; listing the scope's memories."
                    : "This scope has no memories yet.";
            }

            ctx.ContextText = sb.Length > 0 ? sb.ToString() : "(none)";
            ctx.Count = ctx.Citations.Count;
            return ctx;
        }

        private static void AppendContextItem(StringBuilder sb, ContextResult ctx, string? slug, string? title, string? snippet, double? score)
        {
            sb.Append("- [").Append(slug ?? "memory").Append("] ");
            if (!string.IsNullOrEmpty(title)) sb.Append(title).Append(": ");
            sb.Append(snippet).Append('\n');
            ctx.Citations.Add(new ChatCitation { Slug = slug, Title = title, Score = score ?? 0.0 });
            ctx.HitPayloads.Add(new { slug = slug, title = title, score = score ?? 0.0, snippet = snippet });
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        /// <summary>
        /// Return the index up to which the buffer can be emitted without splitting a marker that may continue
        /// in the next chunk: if the buffer ends with a prefix of <paramref name="marker"/>, hold that prefix
        /// back as a tail.
        /// </summary>
        private static int SafeEmitBoundary(string buffer, string marker)
        {
            int max = Math.Min(marker.Length - 1, buffer.Length);
            for (int length = max; length > 0; length--)
            {
                if (string.Compare(buffer, buffer.Length - length, marker, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return buffer.Length - length;
                }
            }

            return buffer.Length;
        }

        private sealed class ContextResult
        {
            public string ContextText { get; set; } = "(none)";
            public List<ChatCitation> Citations { get; } = new List<ChatCitation>();
            public List<object> HitPayloads { get; } = new List<object>();
            public SearchModeEnum Mode { get; set; } = SearchModeEnum.Keyword;
            public string ModeLabel { get; set; } = string.Empty;
            public string? Notice { get; set; } = null;
            public int Count { get; set; } = 0;
        }

        #endregion
    }
}
