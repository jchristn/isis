namespace Isis.Core.Recall
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Stores;
    using TextChunker.Chunking;
    using TextChunker.Models;
    using TextChunker.Tokenization;
    using TcApiFormat = TextChunker.Enums.ApiFormatEnum;
    using TcChunkStrategy = TextChunker.Enums.ChunkStrategyEnum;
    using TcTokenizerKind = TextChunker.Enums.TokenizerKindEnum;

    /// <summary>
    /// Splits a memory body into embeddable chunks that fit the scope's embedding model token budget.
    /// Token counting and budget resolution are performed locally by <c>TextChunker</c> (no model call): the
    /// embedding endpoint's API format and model name resolve a tokenizer family and input budget, which is
    /// used both to decide whether a body overflows and to size the chunks. A small body yields a single chunk
    /// carrying the whole body; an oversized body (or any body, when the scope forces chunking) is split into
    /// ordinal chunks. Chunk offsets are relative to the full body.
    /// </summary>
    public static class MemoryChunker
    {
        #region Public-Members

        /// <summary>
        /// Largest fraction of the per-chunk token budget a chunk header may use. Default 0.25; minimum 0.0 (effectively
        /// no header), maximum 0.5 so the body always keeps at least half of each chunk. Longer headers are truncated.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0, 0.5].</exception>
        public static double HeaderBudgetFraction
        {
            get
            {
                return _HeaderBudgetFraction;
            }
            set
            {
                if (value < 0.0 || value > 0.5) throw new ArgumentOutOfRangeException(nameof(HeaderBudgetFraction), "Header budget fraction must be in [0, 0.5].");
                _HeaderBudgetFraction = value;
            }
        }

        #endregion

        #region Private-Members

        private const string _HeaderSeparator = "\n\n";
        private static double _HeaderBudgetFraction = 0.25;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Chunk a memory body for the scope's embedding endpoint.
        /// </summary>
        /// <param name="scope">The owning scope (supplies the chunking mode, strategy, and per-chunk budget).</param>
        /// <param name="endpoint">The scope's embedding endpoint (supplies the API format, model, and optional
        /// max-input-token override used to resolve the token budget).</param>
        /// <param name="body">The full memory body to chunk.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ordered chunks. Always at least one chunk (the whole body) for a non-empty body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<IReadOnlyList<MemoryChunk>> ChunkAsync(Scope scope, ModelEndpoint endpoint, string body, CancellationToken token = default)
        {
            return await ChunkAsync(scope, endpoint, body, 1.0, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Chunk a memory body for the scope's embedding endpoint using a fraction of the resolved token budget.
        /// Used to re-chunk more finely when the endpoint still rejects a chunk as too long.
        /// </summary>
        /// <param name="scope">The owning scope (supplies the chunking mode, strategy, and per-chunk budget).</param>
        /// <param name="endpoint">The scope's embedding endpoint (supplies the API format, model, and optional
        /// max-input-token override used to resolve the token budget).</param>
        /// <param name="body">The full memory body to chunk.</param>
        /// <param name="budgetScale">Fraction of the resolved per-chunk budget to use, in (0, 1].</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ordered chunks. Always at least one chunk (the whole body) for a non-empty body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when budgetScale is not in (0, 1].</exception>
        public static async Task<IReadOnlyList<MemoryChunk>> ChunkAsync(Scope scope, ModelEndpoint endpoint, string body, double budgetScale, CancellationToken token = default)
        {
            return await ChunkAsync(scope, endpoint, body, null, budgetScale, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Chunk a memory body and prefix every chunk's embedding text with a header (typically the memory's title and
        /// summary), so each chunk is embedded with the context of the memory it belongs to. The header's tokens are
        /// reserved from the per-chunk budget; a header longer than <see cref="HeaderBudgetFraction"/> of the budget is
        /// truncated at a word boundary. Chunk <see cref="MemoryChunk.Text"/> and offsets still refer to the body alone.
        /// </summary>
        /// <param name="scope">The owning scope (supplies the chunking mode, strategy, and per-chunk budget).</param>
        /// <param name="endpoint">The scope's embedding endpoint (supplies the API format, model, and optional
        /// max-input-token override used to resolve the token budget).</param>
        /// <param name="body">The full memory body to chunk.</param>
        /// <param name="header">Optional header to embed ahead of each chunk. Null or blank embeds the chunk text alone.</param>
        /// <param name="budgetScale">Fraction of the resolved per-chunk budget to use, in (0, 1].</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ordered chunks. Always at least one chunk (the whole body) for a non-empty body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when budgetScale is not in (0, 1].</exception>
        public static async Task<IReadOnlyList<MemoryChunk>> ChunkAsync(Scope scope, ModelEndpoint endpoint, string body, string? header, double budgetScale, CancellationToken token = default)
        {
            if (budgetScale <= 0.0 || budgetScale > 1.0) throw new ArgumentOutOfRangeException(nameof(budgetScale));
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (body == null) throw new ArgumentNullException(nameof(body));

            string cleanHeader = string.IsNullOrWhiteSpace(header) ? string.Empty : header!.Trim();
            if (body.Length == 0 || scope.ChunkingMode == ChunkingModeEnum.Off)
            {
                return WithHeader(new List<MemoryChunk> { WholeBody(body) }, cleanHeader);
            }

            TcApiFormat apiFormat = MapApiFormat(endpoint.ApiFormat);
            string modelId = string.IsNullOrEmpty(endpoint.Model) ? "default" : endpoint.Model!;
            int? budgetOverride = endpoint.MaxInputTokens > 0 ? endpoint.MaxInputTokens : (int?)null;

            TokenizationProfileResolver resolver = new TokenizationProfileResolver(null!);
            ResolvedTokenizationProfile profile = await resolver
                .ResolveAsync(TcTokenizerKind.Auto, apiFormat, modelId, budgetOverride, false, token)
                .ConfigureAwait(false);
            ITokenizerAdapter tokenizer = TokenizerAdapterFactory.Create(profile);

            // Reserve the tokenizer's framing tokens: an encoder model (BERT WordPiece) frames every input with
            // [CLS]/[SEP], which count against the model's context window. TextChunker (>= 0.2.2) already reserves
            // these in EffectiveInputBudget, so this is a no-op there; it stays as a backstop that tops up only the
            // shortfall if the profile under-reserves, and never double-counts a reservation the library made.
            int rawBudget = profile.EffectiveInputBudget > 0 ? profile.EffectiveInputBudget : 512;
            int extraReserve = Math.Max(0, EncoderSpecialTokenReserve(profile.TokenizerKind) - Math.Max(0, profile.ReservedInputTokens));
            int budget = Math.Max(1, rawBudget - extraReserve - TokenizerMismatchMargin(rawBudget, budgetOverride.HasValue));
            int perChunk = scope.ChunkMaxTokens > 0 ? Math.Min(scope.ChunkMaxTokens, budget) : budget;
            if (budgetScale < 1.0) perChunk = Math.Max(1, (int)Math.Floor(perChunk * budgetScale));

            // Reserve room for the header (plus its separator) in every chunk, truncating an oversized header.
            if (cleanHeader.Length > 0)
            {
                int maxHeaderTokens = Math.Max(1, (int)Math.Floor(perChunk * HeaderBudgetFraction));
                cleanHeader = TruncateToTokens(tokenizer, cleanHeader, maxHeaderTokens);
                int headerTokens = cleanHeader.Length > 0 ? tokenizer.CountTokens(cleanHeader + _HeaderSeparator) : 0;
                perChunk = Math.Max(1, perChunk - headerTokens);
            }

            // TextChunker can place a split inside a UTF-16 surrogate pair (an emoji or other astral character), and
            // the half-pair then fails Unicode normalization inside the tokenizer. Chunk a same-length stand-in where
            // every surrogate becomes U+FFFD, then slice the real chunks out of the original text by offset, nudging
            // any boundary that would land inside a pair. Offsets line up because the stand-in has the same length.
            bool hasSurrogates = ContainsSurrogates(body);
            string chunkSource = hasSurrogates ? SurrogateStandIn(body) : body;

            if (scope.ChunkingMode == ChunkingModeEnum.OnOverflow)
            {
                int bodyTokens = tokenizer.CountTokens(chunkSource);
                if (bodyTokens <= perChunk) return WithHeader(new List<MemoryChunk> { WholeBody(body) }, cleanHeader);
            }

            ChunkingOptions options = new ChunkingOptions
            {
                Strategy = MapStrategy(scope.ChunkStrategy),
                MaxTokens = perChunk < 1 ? 1 : perChunk,
                OverlapCount = scope.ChunkOverlapTokens,
                ComputeOffsets = true
            };

            IReadOnlyList<Chunk> produced = new Chunker(tokenizer).Chunk(chunkSource, options);
            if (produced.Count == 0) return WithHeader(new List<MemoryChunk> { WholeBody(body) }, cleanHeader);

            List<MemoryChunk> chunks = new List<MemoryChunk>(produced.Count);
            for (int i = 0; i < produced.Count; i++)
            {
                Chunk source = produced[i];
                MemoryChunk chunk = new MemoryChunk
                {
                    Ordinal = chunks.Count,
                    Text = source.Text,
                    StartOffset = source.StartOffset,
                    EndOffset = source.EndOffset
                };
                if (hasSurrogates) RestoreOriginalText(body, chunkSource, chunk);

                // Fixed-token chunking with overlap can emit a run of short trailing chunks that repeat the end of the
                // previous chunk. They add nothing (every character is already in the previous chunk) but they are
                // embedded and stored, and they crowd search results, so drop them.
                if (chunks.Count > 0 && chunks[chunks.Count - 1].Text.Contains(chunk.Text, StringComparison.Ordinal)) continue;
                chunks.Add(chunk);
            }

            return WithHeader(chunks, cleanHeader);
        }

        #endregion

        #region Private-Methods

        private static bool ContainsSurrogates(string text)
        {
            foreach (char c in text)
            {
                if (char.IsSurrogate(c)) return true;
            }

            return false;
        }

        private static string SurrogateStandIn(string text)
        {
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsSurrogate(chars[i])) chars[i] = '\uFFFD';
            }

            return new string(chars);
        }

        private static void RestoreOriginalText(string body, string standIn, MemoryChunk chunk)
        {
            // The chunker reports -1 offsets when it cannot place a chunk (it searches forward only). The chunk text is a
            // literal substring of the stand-in, so locate it there; the last occurrence is the right one for the
            // trailing chunks that are the usual cause. If it still cannot be placed, keep the stand-in text: every
            // character is intact except astral characters, which read as U+FFFD.
            if (chunk.StartOffset < 0 || chunk.EndOffset < chunk.StartOffset || chunk.EndOffset > body.Length)
            {
                int found = standIn.LastIndexOf(chunk.Text, StringComparison.Ordinal);
                if (found < 0) return;
                chunk.StartOffset = found;
                chunk.EndOffset = found + chunk.Text.Length;
            }

            // Widen each boundary that splits a surrogate pair so the pair stays whole.

            int start = chunk.StartOffset;
            int end = chunk.EndOffset;
            if (start > 0 && start < body.Length && char.IsLowSurrogate(body[start]) && char.IsHighSurrogate(body[start - 1])) start--;
            if (end > 0 && end < body.Length && char.IsHighSurrogate(body[end - 1]) && char.IsLowSurrogate(body[end])) end++;

            chunk.StartOffset = start;
            chunk.EndOffset = end;
            chunk.Text = body.Substring(start, end - start);
        }

        private static List<MemoryChunk> WithHeader(List<MemoryChunk> chunks, string header)
        {
            if (header.Length == 0) return chunks;
            foreach (MemoryChunk chunk in chunks)
            {
                chunk.EmbeddingText = header + _HeaderSeparator + chunk.Text;
            }

            return chunks;
        }

        private static string TruncateToTokens(ITokenizerAdapter tokenizer, string text, int maxTokens)
        {
            if (tokenizer.CountTokens(text) <= maxTokens) return text;

            // Binary search the longest word-boundary prefix that fits.
            string[] words = text.Split(' ');
            int low = 0;
            int high = words.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (tokenizer.CountTokens(string.Join(' ', words, 0, mid)) <= maxTokens) low = mid;
                else high = mid - 1;
            }

            return low == 0 ? string.Empty : string.Join(' ', words, 0, low);
        }

        private static MemoryChunk WholeBody(string body)
        {
            return new MemoryChunk { Ordinal = 0, Text = body, StartOffset = 0, EndOffset = body.Length };
        }

        private static TcApiFormat MapApiFormat(ApiFormatEnum format)
        {
            switch (format)
            {
                case ApiFormatEnum.Ollama: return TcApiFormat.Ollama;
                case ApiFormatEnum.OpenAI: return TcApiFormat.OpenAI;
                case ApiFormatEnum.VLlm: return TcApiFormat.VLLM;
                case ApiFormatEnum.Gemini: return TcApiFormat.Gemini;
                default: return TcApiFormat.Unknown;
            }
        }

        private static TcChunkStrategy MapStrategy(string strategy)
        {
            if (!string.IsNullOrEmpty(strategy) && Enum.TryParse(strategy, true, out TcChunkStrategy parsed)) return parsed;
            return TcChunkStrategy.FixedTokenCount;
        }

        // The local tokenizer is a close but not exact match for the serving runtime's: measured against Ollama's
        // all-minilm on scientific text (symbols, Greek letters, numbers), Ollama counted up to ~7 more tokens than
        // the WordPiece vocabulary, so chunks sized to the full 254-token budget were rejected ~20% of the time.
        // Keep a small margin under a model budget the library resolved on its own. An explicit MaxInputTokens
        // override is the operator's statement of the real limit and is used as-is.
        private static int TokenizerMismatchMargin(int budget, bool explicitOverride)
        {
            if (explicitOverride) return 0;
            return Math.Max(4, (int)Math.Ceiling(budget * 0.04));
        }

        // WordPiece encoders (BERT-family embedding models) frame each sequence with [CLS] and [SEP]; those two
        // tokens count against the model's context window, so reserve them. tiktoken families (cl100k/o200k) used
        // by OpenAI-style embeddings do not add framing tokens that reduce the usable budget here.
        private static int EncoderSpecialTokenReserve(TcTokenizerKind kind)
        {
            return kind == TcTokenizerKind.BertWordPiece ? 2 : 0;
        }

        #endregion
    }
}
