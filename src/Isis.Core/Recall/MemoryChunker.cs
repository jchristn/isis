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
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (body == null) throw new ArgumentNullException(nameof(body));

            if (body.Length == 0 || scope.ChunkingMode == ChunkingModeEnum.Off)
            {
                return new List<MemoryChunk> { WholeBody(body) };
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
            int budget = Math.Max(1, rawBudget - extraReserve);
            int perChunk = scope.ChunkMaxTokens > 0 ? Math.Min(scope.ChunkMaxTokens, budget) : budget;

            if (scope.ChunkingMode == ChunkingModeEnum.OnOverflow)
            {
                int bodyTokens = tokenizer.CountTokens(body);
                if (bodyTokens <= perChunk) return new List<MemoryChunk> { WholeBody(body) };
            }

            ChunkingOptions options = new ChunkingOptions
            {
                Strategy = MapStrategy(scope.ChunkStrategy),
                MaxTokens = perChunk < 1 ? 1 : perChunk,
                OverlapCount = scope.ChunkOverlapTokens,
                ComputeOffsets = true
            };

            IReadOnlyList<Chunk> produced = new Chunker(tokenizer).Chunk(body, options);
            if (produced.Count == 0) return new List<MemoryChunk> { WholeBody(body) };

            List<MemoryChunk> chunks = new List<MemoryChunk>(produced.Count);
            for (int i = 0; i < produced.Count; i++)
            {
                Chunk source = produced[i];
                chunks.Add(new MemoryChunk
                {
                    Ordinal = i,
                    Text = source.Text,
                    StartOffset = source.StartOffset,
                    EndOffset = source.EndOffset
                });
            }

            return chunks;
        }

        #endregion

        #region Private-Methods

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
