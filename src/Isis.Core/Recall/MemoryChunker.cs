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

        /// <summary>
        /// When a scope does not set <c>ChunkMaxTokens</c>, the fraction of the model's usable budget each chunk is
        /// sized to, and the threshold above which a body is split. Default 0.75, minimum 0.1, maximum 1.0. Chunks well
        /// under the model limit keep details from being diluted by the text around them: on the Atlas benchmark,
        /// all-minilm chunks of about 190 tokens scored 0.827 Hybrid nDCG@10 against 0.802 at the full 251.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0.1, 1.0].</exception>
        public static double DefaultChunkFraction
        {
            get
            {
                return _DefaultChunkFraction;
            }
            set
            {
                if (value < 0.1 || value > 1.0) throw new ArgumentOutOfRangeException(nameof(DefaultChunkFraction), "Default chunk fraction must be in [0.1, 1.0].");
                _DefaultChunkFraction = value;
            }
        }

        /// <summary>
        /// When a scope does not set <c>ChunkMaxTokens</c>, the largest default chunk in tokens, so models with long
        /// context windows still get retrieval-sized chunks. Default 256, minimum 16.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 16.</exception>
        public static int DefaultChunkMaxTokens
        {
            get
            {
                return _DefaultChunkMaxTokens;
            }
            set
            {
                if (value < 16) throw new ArgumentOutOfRangeException(nameof(DefaultChunkMaxTokens), "Default chunk max tokens must be at least 16.");
                _DefaultChunkMaxTokens = value;
            }
        }

        /// <summary>
        /// Fraction of an automatically resolved model budget held back in case the serving runtime counts a few more
        /// tokens than the local tokenizer. Default 0.01 (at least 2 tokens); minimum 0.0, maximum 0.25. Not applied
        /// when the endpoint sets an explicit MaxInputTokens. TextChunker 0.3 counts WordPiece tokens the way embedding
        /// runtimes do (measured never lower than Ollama), so the margin is small, and an embedding rejected as too
        /// long is still re-chunked at a smaller budget.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0, 0.25].</exception>
        public static double TokenizerMarginFraction
        {
            get
            {
                return _TokenizerMarginFraction;
            }
            set
            {
                if (value < 0.0 || value > 0.25) throw new ArgumentOutOfRangeException(nameof(TokenizerMarginFraction), "Tokenizer margin fraction must be in [0, 0.25].");
                _TokenizerMarginFraction = value;
            }
        }

        #endregion

        #region Private-Members

        private const string _HeaderSeparator = "\n\n";
        private static double _HeaderBudgetFraction = 0.25;
        private static double _TokenizerMarginFraction = 0.01;
        private static double _DefaultChunkFraction = 0.75;
        private static int _DefaultChunkMaxTokens = 256;

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
            // The model's task prefix for stored content (for example nomic's "search_document: ") is embedded with
            // every chunk, so it comes out of the budget first.
            string documentPrefix = EmbeddingPrefixRegistry.For(endpoint.Model, EmbeddingPurposeEnum.Document);
            if (documentPrefix.Length > 0) budget = Math.Max(1, budget - tokenizer.CountTokens(documentPrefix));

            int perChunk = scope.ChunkMaxTokens > 0
                ? Math.Min(scope.ChunkMaxTokens, budget)
                : Math.Max(1, Math.Min(_DefaultChunkMaxTokens, (int)Math.Floor(budget * _DefaultChunkFraction)));
            if (budgetScale < 1.0) perChunk = Math.Max(1, (int)Math.Floor(perChunk * budgetScale));

            // Reserve room for the header (plus its separator) in every chunk, truncating an oversized header.
            if (cleanHeader.Length > 0)
            {
                int maxHeaderTokens = Math.Max(1, (int)Math.Floor(perChunk * HeaderBudgetFraction));
                cleanHeader = TruncateToTokens(tokenizer, cleanHeader, maxHeaderTokens);
                int headerTokens = cleanHeader.Length > 0 ? tokenizer.CountTokens(cleanHeader + _HeaderSeparator) : 0;
                perChunk = Math.Max(1, perChunk - headerTokens);
            }

            if (scope.ChunkingMode == ChunkingModeEnum.OnOverflow)
            {
                int bodyTokens = tokenizer.CountTokens(body);
                if (bodyTokens <= perChunk) return WithHeader(new List<MemoryChunk> { WholeBody(body) }, cleanHeader);
            }

            ChunkingOptions options = new ChunkingOptions
            {
                Strategy = MapStrategy(scope.ChunkStrategy),
                MaxTokens = perChunk < 1 ? 1 : perChunk,
                OverlapCount = scope.ChunkOverlapTokens,
                ComputeOffsets = true
            };

            // TextChunker 0.3 chunks spans of the source text: cuts never split a surrogate pair or grapheme cluster,
            // offsets are exact, and no chunk is contained in the one before it, so chunks are used as produced.
            IReadOnlyList<Chunk> produced = new Chunker(tokenizer).Chunk(body, options);
            if (produced.Count == 0) return WithHeader(new List<MemoryChunk> { WholeBody(body) }, cleanHeader);

            List<MemoryChunk> chunks = new List<MemoryChunk>(produced.Count);
            for (int i = 0; i < produced.Count; i++)
            {
                Chunk source = produced[i];
                chunks.Add(new MemoryChunk
                {
                    Ordinal = chunks.Count,
                    Text = source.Text,
                    StartOffset = source.StartOffset,
                    EndOffset = source.EndOffset
                });
            }

            return WithHeader(chunks, cleanHeader);
        }

        #endregion

        #region Private-Methods

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

        // Keep a small margin under a model budget the library resolved on its own. An explicit MaxInputTokens
        // override is the operator's statement of the real limit and is used as-is.
        private static int TokenizerMismatchMargin(int budget, bool explicitOverride)
        {
            if (explicitOverride || _TokenizerMarginFraction <= 0.0) return 0;
            return Math.Max(2, (int)Math.Ceiling(budget * _TokenizerMarginFraction));
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
