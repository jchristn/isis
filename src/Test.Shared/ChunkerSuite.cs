namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone test suite exercising <see cref="MemoryChunker"/>: the local (no model call) token-budget
    /// resolution and chunk splitting that feeds embedding. These tests use only the chunker's real API and the
    /// bundled tokenizer profiles; they touch no external services.
    /// </summary>
    public static class ChunkerSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the chunker test suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "chunker",
                "Isis Chunker Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Async("chunker", "small-single", "A small body under budget yields a single whole-body chunk", SmallSingleAsync),
                    TestCase.Async("chunker", "overflow-splits", "An oversized body splits into contiguous ordinal chunks", OverflowSplitsAsync),
                    TestCase.Async("chunker", "off-keeps-whole", "ChunkingMode.Off keeps an oversized body whole", OffKeepsWholeAsync),
                    TestCase.Async("chunker", "always-splits-small", "ChunkingMode.Always with a small budget splits a small body", AlwaysSplitsSmallAsync),
                    TestCase.Async("chunker", "smaller-budget-more-chunks", "A smaller per-chunk budget produces more chunks", SmallerBudgetMoreChunksAsync),
                    TestCase.Async("chunker", "endpoint-maxinputtokens-caps-budget", "A low endpoint MaxInputTokens caps the chunk budget", EndpointMaxInputTokensCapsBudgetAsync),
                    TestCase.Async("chunker", "empty-body-single", "An empty body yields a single empty chunk", EmptyBodySingleAsync)
                });
        }

        #endregion

        #region Private-Methods

        private static async Task SmallSingleAsync()
        {
            Scope scope = Scope(ChunkingModeEnum.OnOverflow);
            string body = "Control the centerline; posture and framing win positions.";
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(scope, Endpoint(), body).ConfigureAwait(false);

            TestCase.Require(chunks.Count == 1, "A small body should produce exactly one chunk, got " + chunks.Count + ".");
            TestCase.Require(chunks[0].Ordinal == 0, "The single chunk should have ordinal 0.");
            TestCase.Require(chunks[0].Text == body, "The single chunk should carry the whole body verbatim.");
        }

        private static async Task OverflowSplitsAsync()
        {
            Scope scope = Scope(ChunkingModeEnum.OnOverflow);
            string body = Oversized();
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(scope, Endpoint(), body).ConfigureAwait(false);

            TestCase.Require(chunks.Count > 1, "An oversized body should split into multiple chunks, got " + chunks.Count + ".");
            AssertContiguous(chunks);
            foreach (MemoryChunk chunk in chunks)
            {
                TestCase.Require(!string.IsNullOrEmpty(chunk.Text), "Every chunk should carry text.");
                TestCase.Require(chunk.StartOffset >= 0 && chunk.EndOffset <= body.Length, "Chunk offsets should lie within the body.");
            }
        }

        private static async Task OffKeepsWholeAsync()
        {
            Scope scope = Scope(ChunkingModeEnum.Off);
            string body = Oversized();
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(scope, Endpoint(), body).ConfigureAwait(false);

            TestCase.Require(chunks.Count == 1, "ChunkingMode.Off should keep the body whole even when oversized, got " + chunks.Count + " chunks.");
            TestCase.Require(chunks[0].Text == body, "The whole-body chunk should equal the input body.");
        }

        private static async Task AlwaysSplitsSmallAsync()
        {
            Scope scope = Scope(ChunkingModeEnum.Always);
            scope.ChunkMaxTokens = 8;
            scope.ChunkOverlapTokens = 0;
            string body = "Control the centerline; posture and framing win positions in every exchange you take.";
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(scope, Endpoint(), body).ConfigureAwait(false);

            TestCase.Require(chunks.Count > 1, "ChunkingMode.Always with an 8-token budget should split a small body, got " + chunks.Count + ".");
            AssertContiguous(chunks);
        }

        private static async Task SmallerBudgetMoreChunksAsync()
        {
            string body = Oversized();
            Scope wide = Scope(ChunkingModeEnum.Always);
            Scope narrow = Scope(ChunkingModeEnum.Always);
            narrow.ChunkMaxTokens = 64;

            IReadOnlyList<MemoryChunk> wideChunks = await MemoryChunker.ChunkAsync(wide, Endpoint(), body).ConfigureAwait(false);
            IReadOnlyList<MemoryChunk> narrowChunks = await MemoryChunker.ChunkAsync(narrow, Endpoint(), body).ConfigureAwait(false);

            TestCase.Require(narrowChunks.Count > wideChunks.Count, "A 64-token budget should produce more chunks than the full model budget (" + narrowChunks.Count + " vs " + wideChunks.Count + ").");
        }

        private static async Task EndpointMaxInputTokensCapsBudgetAsync()
        {
            string body = Oversized();
            Scope scope = Scope(ChunkingModeEnum.Always);

            // Auto (MaxInputTokens = 0) uses the model's resolved budget; a low override must chunk more finely,
            // which is the lever that fits all-minilm's real 256-token context.
            IReadOnlyList<MemoryChunk> auto = await MemoryChunker.ChunkAsync(scope, Endpoint(0), body).ConfigureAwait(false);
            IReadOnlyList<MemoryChunk> capped = await MemoryChunker.ChunkAsync(scope, Endpoint(64), body).ConfigureAwait(false);

            TestCase.Require(capped.Count > auto.Count, "A 64-token MaxInputTokens override should produce more chunks than the auto budget (" + capped.Count + " vs " + auto.Count + ").");
            AssertContiguous(capped);
        }

        private static async Task EmptyBodySingleAsync()
        {
            Scope scope = Scope(ChunkingModeEnum.Always);
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(scope, Endpoint(), string.Empty).ConfigureAwait(false);

            TestCase.Require(chunks.Count == 1, "An empty body should still produce a single chunk.");
            TestCase.Require(chunks[0].Text.Length == 0, "The single chunk for an empty body should be empty.");
        }

        private static void AssertContiguous(IReadOnlyList<MemoryChunk> chunks)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                TestCase.Require(chunks[i].Ordinal == i, "Chunk ordinals should be contiguous from 0; expected " + i + " but saw " + chunks[i].Ordinal + ".");
            }
        }

        // all-minilm resolves locally to BERT WordPiece (no model call); ~1000 tokens overflows any real budget.
        private static string Oversized()
        {
            return string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog near the river bank. ", 120));
        }

        private static ModelEndpoint Endpoint(int maxInputTokens = 0)
        {
            return new ModelEndpoint { TenantId = "ten_x", Name = "embed", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.Ollama, Model = "all-minilm", BaseUrl = "http://127.0.0.1:11434", MaxInputTokens = maxInputTokens };
        }

        private static Scope Scope(ChunkingModeEnum mode)
        {
            return new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.RecallDb, ChunkingMode = mode, ChunkStrategy = "FixedTokenCount", ChunkOverlapTokens = 16 };
        }

        #endregion
    }
}
