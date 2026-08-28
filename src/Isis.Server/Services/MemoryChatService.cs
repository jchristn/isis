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

            MemorySearchQuery query = new MemorySearchQuery { QueryText = question, Mode = SearchModeEnum.Hybrid, TopK = topK < 1 ? 5 : topK };
            MemorySearchResult retrieval = await _MemoryService.SearchAsync(scope, query, token).ConfigureAwait(false);

            ChatAnswer answer = new ChatAnswer();
            answer.RetrievalMode = retrieval.EffectiveMode;
            answer.Notice = retrieval.Notice;

            StringBuilder context = new StringBuilder();
            foreach (MemorySearchHit hit in retrieval.Hits)
            {
                context.Append("- [").Append(hit.Slug ?? "memory").Append("] ");
                if (!string.IsNullOrEmpty(hit.Title)) context.Append(hit.Title).Append(": ");
                context.Append(hit.Snippet).Append('\n');

                answer.Citations.Add(new ChatCitation { Slug = hit.Slug, Title = hit.Title, Score = hit.Score });
            }

            if (retrieval.Hits.Count == 0)
            {
                answer.Notice = string.IsNullOrEmpty(answer.Notice) ? "No memories matched the question." : answer.Notice;
            }

            string systemPrompt =
                "You are a memory assistant. Answer the user's question using only the provided memories. " +
                "Cite the memories you use by their slug in square brackets. If the memories do not contain the answer, say so plainly.";
            string userPrompt = "Question: " + question + "\n\nMemories:\n" + (context.Length > 0 ? context.ToString() : "(none)");

            answer.Answer = await _InferenceService.CompleteAsync(inferenceEndpoint, systemPrompt, userPrompt, token).ConfigureAwait(false);
            return answer;
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

            MemorySearchQuery query = new MemorySearchQuery { QueryText = question, Mode = SearchModeEnum.Hybrid, TopK = topK < 1 ? 5 : topK };
            MemorySearchResult retrieval = await _MemoryService.SearchAsync(scope, query, token).ConfigureAwait(false);

            List<ChatCitation> citations = new List<ChatCitation>();
            List<object> hitPayloads = new List<object>();
            StringBuilder context = new StringBuilder();
            foreach (MemorySearchHit hit in retrieval.Hits)
            {
                context.Append("- [").Append(hit.Slug ?? "memory").Append("] ");
                if (!string.IsNullOrEmpty(hit.Title)) context.Append(hit.Title).Append(": ");
                context.Append(hit.Snippet).Append('\n');
                citations.Add(new ChatCitation { Slug = hit.Slug, Title = hit.Title, Score = hit.Score });
                hitPayloads.Add(new { slug = hit.Slug, title = hit.Title, score = hit.Score, snippet = hit.Snippet });
            }

            string? notice = retrieval.Notice;
            if (retrieval.Hits.Count == 0 && string.IsNullOrEmpty(notice)) notice = "No memories matched the question.";

            await emit(new { type = "retrieval", mode = retrieval.EffectiveMode.ToString(), hits = hitPayloads, notice = notice }, token).ConfigureAwait(false);

            string systemPrompt =
                "You are a memory assistant. Answer the user's question using only the provided memories. " +
                "Cite the memories you use by their slug in square brackets. If the memories do not contain the answer, say so plainly.";
            string userPrompt = "Question: " + question + "\n\nMemories:\n" + (context.Length > 0 ? context.ToString() : "(none)");

            StringBuilder answerBuilder = new StringBuilder();
            Stopwatch stopwatch = Stopwatch.StartNew();
            double ttftMs = 0.0;
            bool firstAnswer = true;
            int promptTokens = 0;
            int completionTokens = 0;
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
            }

            if (!string.IsNullOrEmpty(tail)) await EmitSegmentAsync(inThink, tail).ConfigureAwait(false);

            stopwatch.Stop();
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            double generationMs = Math.Max(0.0, totalMs - ttftMs);
            double tokensPerSecond = completionTokens > 0 && generationMs > 0.0 ? completionTokens / (generationMs / 1000.0) : 0.0;

            await emit(new
            {
                type = "complete",
                answer = answerBuilder.ToString(),
                citations = citations,
                retrievalMode = retrieval.EffectiveMode.ToString(),
                notice = notice,
                model = inferenceEndpoint.Model,
                promptTokens = promptTokens,
                completionTokens = completionTokens,
                totalTokens = promptTokens + completionTokens,
                timeToFirstTokenMs = ttftMs,
                generationMs = generationMs,
                tokensPerSecond = tokensPerSecond
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

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

        #endregion
    }
}
