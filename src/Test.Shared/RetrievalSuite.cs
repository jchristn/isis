namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.Core.Stores.RecallDb;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using RecallDb.Sdk.Models;
    using TextChunker.Tokenization;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the retrieval improvements in RETRIEVAL_IMPROVEMENTS.md: hybrid fusion (normalized scores,
    /// per-leg evidence, recency), chunk headers, the search score threshold, the chat retrieval depth and prompt, the
    /// update-not-duplicate guidance in the default instructions, and ingest robustness against invalid Unicode.
    /// </summary>
    public static class RetrievalSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the retrieval test suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "retrieval",
                "Isis Retrieval Improvements Suite",
                new List<TestCaseDescriptor>
                {
                    TestCase.Sync("retrieval", "fusion-top-in-both-scores-one", "Fusion: a document ranked first in both legs scores 1.0", FusionTopInBothScoresOne),
                    TestCase.Sync("retrieval", "fusion-union-keeps-single-leg", "Fusion: documents found by only one leg are kept, with that leg's evidence", FusionUnionKeepsSingleLeg),
                    TestCase.Sync("retrieval", "fusion-text-weight-extremes", "Fusion: text weight 0 follows the vector order and 1 follows the text order", FusionTextWeightExtremes),
                    TestCase.Sync("retrieval", "fusion-scores-normalized", "Fusion: every fused score is within [0, 1]", FusionScoresNormalized),
                    TestCase.Sync("retrieval", "fusion-recency-breaks-tie", "Fusion: recency ranks the newer of two equally relevant memories first", FusionRecencyBreaksTie),
                    TestCase.Sync("retrieval", "fusion-recency-keeps-relevance", "Fusion: a small recency weight does not override a clearly better match", FusionRecencyKeepsRelevance),
                    TestCase.Sync("retrieval", "fusion-recency-zero-disabled", "Fusion: recency weight 0 applies no recency rank", FusionRecencyZeroDisabled),
                    TestCase.Sync("retrieval", "fusion-recency-per-memory", "Fusion: all chunks of one memory share its recency rank", FusionRecencyPerMemory),
                    TestCase.Sync("retrieval", "fusion-validates-arguments", "Fusion: out-of-range weights and k are rejected", FusionValidatesArguments),
                    TestCase.Async("retrieval", "chunker-header-prefixes-embedding", "Chunker: the header prefixes each chunk's embedding text but not its stored text", ChunkerHeaderPrefixesEmbeddingAsync),
                    TestCase.Async("retrieval", "chunker-header-fits-budget", "Chunker: header plus chunk stays within the auto-resolved budget", ChunkerHeaderFitsBudgetAsync),
                    TestCase.Async("retrieval", "chunker-header-truncated", "Chunker: an oversized header is truncated to the header budget", ChunkerHeaderTruncatedAsync),
                    TestCase.Async("retrieval", "chunker-no-header", "Chunker: without a header the embedding text is null (embed the chunk as is)", ChunkerNoHeaderAsync),
                    TestCase.Sync("retrieval", "chunker-header-fraction-validated", "Chunker: HeaderBudgetFraction rejects values outside [0, 0.5]", ChunkerHeaderFractionValidated),
                    TestCase.Sync("retrieval", "query-recency-and-minscore-defaults", "MemorySearchQuery: RecencyWeight defaults to 0.1 and clamps; MinScore defaults to null", QueryRecencyAndMinScoreDefaults),
                    TestCase.Async("retrieval", "search-min-score-filters", "Search: MinScore drops hits scoring below it", SearchMinScoreFiltersAsync),
                    TestCase.Async("retrieval", "chat-default-topk", "Chat: requests default to the server's retrieval depth (8) and DefaultTopK is validated", ChatDefaultTopKAsync),
                    TestCase.Async("retrieval", "chat-prompt-strict", "Chat: the system prompt forbids unstated facts and requires a not-in-memory answer", ChatPromptStrictAsync),
                    TestCase.Async("retrieval", "chat-history-prompt", "Chat: earlier messages appear in the answer prompt; keyword stores skip the rewrite call", ChatHistoryPromptAsync),
                    TestCase.Sync("retrieval", "instructions-update-not-duplicate", "Default instructions tell agents to update a changed fact instead of adding a duplicate", InstructionsUpdateNotDuplicate),
                    TestCase.Sync("retrieval", "sanitizer-replaces-lone-surrogates", "TextSanitizer replaces unpaired surrogates and keeps valid pairs", SanitizerReplacesLoneSurrogates),
                    TestCase.Async("retrieval", "upsert-with-lone-surrogate", "A memory containing an unpaired surrogate is stored (replaced with U+FFFD) instead of failing", UpsertWithLoneSurrogateAsync),
                    TestCase.Async("retrieval", "chunker-emoji-safe", "Chunker: a long body full of emoji chunks without splitting a surrogate pair", ChunkerEmojiSafeAsync),
                    TestCase.Async("retrieval", "chunker-no-redundant-tail-chunks", "Chunker: no chunk is wholly contained in the chunk before it", ChunkerNoRedundantTailChunksAsync)
                });
        }

        #endregion

        #region Private-Methods

        private static DocumentRecord Doc(string key, double score, DateTime created, string? parent = null)
        {
            DocumentRecord document = new DocumentRecord();
            document.DocumentKey = key;
            document.DocumentId = key;
            document.Score = score;
            document.CreatedUtc = created;
            document.Tags = new Dictionary<string, string> { { "parentKey", parent ?? key } };
            return document;
        }

        private static string Parent(DocumentRecord document)
        {
            return document.Tags != null && document.Tags.TryGetValue("parentKey", out string? parent) ? parent : document.DocumentKey ?? string.Empty;
        }

        private static List<DocumentRecord> Leg(params DocumentRecord[] documents)
        {
            return documents.ToList();
        }

        private static void FusionTopInBothScoresOne()
        {
            DateTime now = DateTime.UtcNow;
            List<FusedDocument> fused = HybridFusion.Fuse(Leg(Doc("a", 0.9, now), Doc("b", 0.8, now)), Leg(Doc("a", 0.3, now), Doc("c", 0.2, now)), 0.5, 0.0, Parent);
            TestCase.Require(fused[0].Document.DocumentKey == "a", "The document ranked first in both legs should fuse first.");
            TestCase.Require(Math.Abs(fused[0].Score - 1.0) < 1e-9, "Ranked first in both legs should score 1.0, got " + fused[0].Score + ".");
            TestCase.Require(fused[0].VectorRank == 1 && fused[0].TextRank == 1, "Both leg ranks should be recorded.");
            TestCase.Require(fused[0].VectorScore == 0.9 && fused[0].TextScore == 0.3, "Both leg scores should be recorded.");
        }

        private static void FusionUnionKeepsSingleLeg()
        {
            DateTime now = DateTime.UtcNow;
            List<FusedDocument> fused = HybridFusion.Fuse(Leg(Doc("v", 0.9, now)), Leg(Doc("t", 0.4, now)), 0.5, 0.0, Parent);
            TestCase.Require(fused.Count == 2, "Both single-leg documents should be kept.");
            FusedDocument v = fused.Single(f => f.Document.DocumentKey == "v");
            FusedDocument t = fused.Single(f => f.Document.DocumentKey == "t");
            TestCase.Require(v.VectorRank == 1 && v.TextRank == null && v.TextScore == null, "The vector-only document has no text evidence.");
            TestCase.Require(t.TextRank == 1 && t.VectorRank == null && t.VectorScore == null, "The text-only document has no vector evidence.");
            TestCase.Require(Math.Abs(v.Score - 0.5) < 1e-9 && Math.Abs(t.Score - 0.5) < 1e-9, "Rank 1 in one of two equally weighted legs scores 0.5.");
        }

        private static void FusionTextWeightExtremes()
        {
            DateTime now = DateTime.UtcNow;
            List<DocumentRecord> vector = Leg(Doc("a", 0.9, now), Doc("b", 0.8, now), Doc("c", 0.7, now));
            List<DocumentRecord> text = Leg(Doc("c", 0.5, now), Doc("b", 0.4, now), Doc("a", 0.3, now));
            List<string> vectorOnly = HybridFusion.Fuse(vector, text, 0.0, 0.0, Parent).Select(f => f.Document.DocumentKey ?? string.Empty).ToList();
            List<string> textOnly = HybridFusion.Fuse(vector, text, 1.0, 0.0, Parent).Select(f => f.Document.DocumentKey ?? string.Empty).ToList();
            TestCase.Require(string.Join(",", vectorOnly) == "a,b,c", "Text weight 0 should follow the vector order, got " + string.Join(",", vectorOnly) + ".");
            TestCase.Require(string.Join(",", textOnly) == "c,b,a", "Text weight 1 should follow the text order, got " + string.Join(",", textOnly) + ".");
        }

        private static void FusionScoresNormalized()
        {
            DateTime now = DateTime.UtcNow;
            List<DocumentRecord> vector = Enumerable.Range(0, 40).Select(i => Doc("v" + i, 1.0 - i / 100.0, now.AddMinutes(-i))).ToList();
            List<DocumentRecord> text = Enumerable.Range(0, 40).Select(i => Doc("v" + (39 - i), 1.0 - i / 100.0, now.AddMinutes(-i))).ToList();
            foreach (double recency in new double[] { 0.0, 0.1, 1.0 })
            {
                foreach (FusedDocument fused in HybridFusion.Fuse(vector, text, 0.3, recency, Parent))
                {
                    TestCase.Require(fused.Score >= 0.0 && fused.Score <= 1.0 + 1e-9, "Fused score out of [0, 1]: " + fused.Score + " at recency " + recency + ".");
                }
            }
        }

        private static void FusionRecencyBreaksTie()
        {
            DateTime now = DateTime.UtcNow;
            // "old" and "new" swap places between the legs, so without recency they tie exactly.
            List<DocumentRecord> vector = Leg(Doc("old", 0.9, now.AddDays(-30)), Doc("new", 0.8, now));
            List<DocumentRecord> text = Leg(Doc("new", 0.5, now), Doc("old", 0.4, now.AddDays(-30)));
            List<FusedDocument> fused = HybridFusion.Fuse(vector, text, 0.5, 0.1, Parent);
            TestCase.Require(fused[0].Document.DocumentKey == "new", "With recency, the newer of two tied memories should rank first.");
            TestCase.Require(fused[0].RecencyRank == 1 && fused[1].RecencyRank == 2, "Recency ranks should be assigned newest first.");
        }

        private static void FusionRecencyKeepsRelevance()
        {
            DateTime now = DateTime.UtcNow;
            // "best" is first in both legs but oldest; "fresh" is the newest but ranked last in both legs.
            List<DocumentRecord> vector = new List<DocumentRecord> { Doc("best", 0.95, now.AddDays(-90)) };
            List<DocumentRecord> text = new List<DocumentRecord> { Doc("best", 0.6, now.AddDays(-90)) };
            for (int i = 0; i < 10; i++)
            {
                vector.Add(Doc("mid" + i, 0.8 - i / 100.0, now.AddDays(-80 + i)));
                text.Add(Doc("mid" + i, 0.5 - i / 100.0, now.AddDays(-80 + i)));
            }

            vector.Add(Doc("fresh", 0.5, now));
            text.Add(Doc("fresh", 0.1, now));
            List<FusedDocument> fused = HybridFusion.Fuse(vector, text, 0.5, 0.1, Parent);
            TestCase.Require(fused[0].Document.DocumentKey == "best", "A small recency weight must not lift a far weaker match above the best one.");
        }

        private static void FusionRecencyZeroDisabled()
        {
            DateTime now = DateTime.UtcNow;
            List<FusedDocument> fused = HybridFusion.Fuse(Leg(Doc("a", 0.9, now)), Leg(Doc("b", 0.5, now.AddDays(-1))), 0.5, 0.0, Parent);
            TestCase.Require(fused.All(f => f.RecencyRank == null), "Recency weight 0 should not assign recency ranks.");
        }

        private static void FusionRecencyPerMemory()
        {
            DateTime now = DateTime.UtcNow;
            List<DocumentRecord> vector = Leg(Doc("m1-c0", 0.9, now, "m1"), Doc("m2", 0.8, now.AddDays(-1)), Doc("m1-c1", 0.7, now, "m1"));
            List<FusedDocument> fused = HybridFusion.Fuse(vector, null, 0.5, 0.2, Parent);
            int m1First = fused.Single(f => f.Document.DocumentKey == "m1-c0").RecencyRank ?? -1;
            int m1Second = fused.Single(f => f.Document.DocumentKey == "m1-c1").RecencyRank ?? -1;
            int m2 = fused.Single(f => f.Document.DocumentKey == "m2").RecencyRank ?? -1;
            TestCase.Require(m1First == 1 && m1Second == 1, "Both chunks of the newest memory should share recency rank 1.");
            TestCase.Require(m2 == 2, "The older memory should have recency rank 2.");
        }

        private static void FusionValidatesArguments()
        {
            TestCase.Throws<ArgumentOutOfRangeException>(() => HybridFusion.Fuse(null, null, 1.5, 0.0, Parent), "Text weight above 1 should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => HybridFusion.Fuse(null, null, 0.5, -0.1, Parent), "Negative recency weight should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => HybridFusion.Fuse(null, null, 0.5, 0.0, Parent, 0), "An RRF constant below 1 should be rejected.");
            TestCase.Throws<ArgumentNullException>(() => HybridFusion.Fuse(null, null, 0.5, 0.0, null!), "A null parent-key function should be rejected.");
            TestCase.Require(HybridFusion.Fuse(null, null, 0.5, 0.1, Parent).Count == 0, "Empty legs fuse to an empty list.");
        }

        private static Scope ChunkScope(ChunkingModeEnum mode)
        {
            return new Scope { TenantId = "ten_x", Name = "s", StoreProvider = StoreProviderEnum.RecallDb, ChunkingMode = mode, ChunkStrategy = "FixedTokenCount", ChunkOverlapTokens = 16 };
        }

        private static ModelEndpoint MiniLm()
        {
            return new ModelEndpoint { TenantId = "ten_x", Name = "embed", Kind = EndpointKindEnum.Embedding, ApiFormat = ApiFormatEnum.Ollama, Model = "all-minilm", BaseUrl = "http://127.0.0.1:11434" };
        }

        private static string LongBody()
        {
            return string.Concat(Enumerable.Repeat("The ingest service retries failed batches with exponential backoff before paging on-call. ", 60));
        }

        private static async Task ChunkerHeaderPrefixesEmbeddingAsync()
        {
            string header = "Ingest retries: how the ingest service handles failed batches";
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), LongBody(), header, 1.0).ConfigureAwait(false);
            TestCase.Require(chunks.Count > 1, "The long body should split.");
            foreach (MemoryChunk chunk in chunks)
            {
                TestCase.Require(chunk.EmbeddingText != null && chunk.EmbeddingText.StartsWith(header, StringComparison.Ordinal), "Each chunk's embedding text should start with the header.");
                TestCase.Require(chunk.EmbeddingText!.EndsWith(chunk.Text, StringComparison.Ordinal), "Each chunk's embedding text should end with the chunk text.");
                TestCase.Require(!chunk.Text.StartsWith(header, StringComparison.Ordinal), "The stored chunk text must not contain the header.");
            }

            IReadOnlyList<MemoryChunk> small = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), "A short memory body.", header, 1.0).ConfigureAwait(false);
            TestCase.Require(small.Count == 1 && small[0].Text == "A short memory body.", "A small body stays one whole-body chunk.");
            TestCase.Require(small[0].EmbeddingText == header + "\n\nA short memory body.", "A single chunk is embedded with the header too.");
        }

        private static async Task ChunkerHeaderFitsBudgetAsync()
        {
            BertWordPieceTokenizerAdapter wordPiece = new BertWordPieceTokenizerAdapter();
            string header = "Ingest retries: how the ingest service handles failed batches and when it pages on-call";
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), LongBody(), header, 1.0).ConfigureAwait(false);
            int largest = chunks.Max(c => wordPiece.CountTokens(c.EmbeddingText ?? c.Text));
            int cap = 254 - Math.Max(2, (int)Math.Ceiling(254 * MemoryChunker.TokenizerMarginFraction));
            TestCase.Require(largest <= cap, "Header plus chunk must stay within the auto-resolved all-minilm budget (<= " + cap + "), largest was " + largest + ".");
        }

        private static async Task ChunkerHeaderTruncatedAsync()
        {
            BertWordPieceTokenizerAdapter wordPiece = new BertWordPieceTokenizerAdapter();
            string header = string.Concat(Enumerable.Repeat("an extremely long and repetitive summary ", 80));
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), LongBody(), header, 1.0).ConfigureAwait(false);
            string embedded = chunks[0].EmbeddingText ?? string.Empty;
            string usedHeader = embedded.Substring(0, embedded.IndexOf("\n\n", StringComparison.Ordinal));
            int headerTokens = wordPiece.CountTokens(usedHeader);
            int cap = 254 - Math.Max(2, (int)Math.Ceiling(254 * MemoryChunker.TokenizerMarginFraction));
            TestCase.Require(headerTokens > 0 && headerTokens <= cap / 4 + 1, "An oversized header should be truncated to about a quarter of the budget, got " + headerTokens + " tokens.");
            int largest = chunks.Max(c => wordPiece.CountTokens(c.EmbeddingText ?? c.Text));
            TestCase.Require(largest <= cap, "Chunks with a truncated header must still fit the budget, largest was " + largest + ".");
        }

        private static async Task ChunkerNoHeaderAsync()
        {
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), LongBody(), "   ", 1.0).ConfigureAwait(false);
            TestCase.Require(chunks.All(c => c.EmbeddingText == null), "A blank header should leave EmbeddingText null.");
            IReadOnlyList<MemoryChunk> legacy = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), LongBody()).ConfigureAwait(false);
            TestCase.Require(legacy.All(c => c.EmbeddingText == null), "The header-less overload should leave EmbeddingText null.");
        }

        private static void ChunkerHeaderFractionValidated()
        {
            double original = MemoryChunker.HeaderBudgetFraction;
            TestCase.Require(Math.Abs(original - 0.25) < 1e-9, "HeaderBudgetFraction should default to 0.25.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => MemoryChunker.HeaderBudgetFraction = 0.6, "A fraction above 0.5 should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => MemoryChunker.HeaderBudgetFraction = -0.1, "A negative fraction should be rejected.");
            TestCase.Require(Math.Abs(MemoryChunker.HeaderBudgetFraction - original) < 1e-9, "A rejected value must not change the setting.");
        }

        private static void QueryRecencyAndMinScoreDefaults()
        {
            MemorySearchQuery query = new MemorySearchQuery();
            TestCase.Require(Math.Abs(query.RecencyWeight - 0.1) < 1e-9, "RecencyWeight should default to 0.1.");
            TestCase.Require(query.MinScore == null, "MinScore should default to null.");
            query.RecencyWeight = 2.0;
            TestCase.Require(query.RecencyWeight == 1.0, "RecencyWeight should clamp to 1.");
            query.RecencyWeight = -1.0;
            TestCase.Require(query.RecencyWeight == 0.0, "RecencyWeight should clamp to 0.");
        }

        private static async Task<FilesystemFixture> FilesystemFixtureAsync(TempSqlite t)
        {
            FilesystemFixture fixture = new FilesystemFixture();
            fixture.Work = Path.Combine(Path.GetTempPath(), "isis-ret-" + Guid.NewGuid().ToString("N"));
            Tenant tenant = await t.Db.Tenants.CreateAsync(new Tenant { Name = "Acme" }).ConfigureAwait(false);
            fixture.Scope = await t.Db.Scopes.CreateAsync(new Scope { TenantId = tenant.Id, Name = "proj", StoreProvider = StoreProviderEnum.Filesystem, TargetPath = fixture.Work }).ConfigureAwait(false);
            fixture.Category = await t.Db.Categories.CreateAsync(new Category { TenantId = tenant.Id, ScopeId = fixture.Scope.Id, Name = "notes" }).ConfigureAwait(false);
            fixture.Service = new MemoryService(t.Db);
            return fixture;
        }

        private static void DeleteWork(string work)
        {
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }
            catch (IOException)
            {
            }
        }

        private static async Task SearchMinScoreFiltersAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture fixture = await FilesystemFixtureAsync(t).ConfigureAwait(false);
            try
            {
                await fixture.Service.UpsertAsync(fixture.Scope, fixture.Category, new Memory { Slug = "strong", Title = "Grip", Body = "grip grip grip: win the grip to win the exchange." }).ConfigureAwait(false);
                await fixture.Service.UpsertAsync(fixture.Scope, fixture.Category, new Memory { Slug = "weak", Title = "Other", Body = "Mentions grip once." }).ConfigureAwait(false);

                MemorySearchResult all = await fixture.Service.SearchAsync(fixture.Scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword }).ConfigureAwait(false);
                TestCase.Require(all.Hits.Count == 2, "Without MinScore both memories should match, got " + all.Hits.Count + ".");

                double threshold = all.Hits.Max(h => h.Score);
                MemorySearchResult filtered = await fixture.Service.SearchAsync(fixture.Scope, new MemorySearchQuery { QueryText = "grip", Mode = SearchModeEnum.Keyword, MinScore = threshold }).ConfigureAwait(false);
                TestCase.Require(filtered.Hits.Count == 1 && filtered.Hits[0].Slug == "strong", "MinScore should keep only the hit at or above the threshold.");
            }
            finally
            {
                DeleteWork(fixture.Work);
            }
        }

        private static async Task ChatDefaultTopKAsync()
        {
            TestCase.Require(new ChatRequest().TopK == 0, "ChatRequest.TopK should default to 0 (use the server default).");
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            using StubResponseHandler handler = new StubResponseHandler("{}");
            MemoryChatService chat = new MemoryChatService(new MemoryService(t.Db), new InferenceService(handler));
            TestCase.Require(chat.DefaultTopK == 8, "MemoryChatService.DefaultTopK should default to 8.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => chat.DefaultTopK = 0, "DefaultTopK 0 should be rejected.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => chat.DefaultTopK = 101, "DefaultTopK above 100 should be rejected.");
        }

        private static async Task ChatHistoryPromptAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture fixture = await FilesystemFixtureAsync(t).ConfigureAwait(false);
            try
            {
                await fixture.Service.UpsertAsync(fixture.Scope, fixture.Category, new Memory { Slug = "db", Title = "Database", Body = "Production Postgres listens on 5432; staging on 6432." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "6432 [db]" } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = fixture.Scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };
                MemoryChatService chat = new MemoryChatService(fixture.Service, inference);
                List<ChatTurn> history = new List<ChatTurn>
                {
                    new ChatTurn { Role = "user", Content = "What port does production Postgres use?" },
                    new ChatTurn { Role = "assistant", Content = "5432 [db]" }
                };
                ChatAnswer answer = await chat.AskAsync(fixture.Scope, endpoint, "and staging?", 0, default, history).ConfigureAwait(false);

                string sent = handler.LastRequestBody ?? string.Empty;
                TestCase.Require(sent.Contains("Conversation so far:", StringComparison.Ordinal) && sent.Contains("User: What port does production Postgres use?", StringComparison.Ordinal), "The answer prompt should show the earlier messages.");
                TestCase.Require(sent.Contains("Question: and staging?", StringComparison.Ordinal), "The answer prompt should keep the question as asked.");
                TestCase.Require(handler.RequestCount == 1 && answer.StandaloneQuestion == null, "A keyword store is given every memory, so no rewrite call should be made.");
            }
            finally
            {
                DeleteWork(fixture.Work);
            }
        }

        private static async Task ChatPromptStrictAsync()
        {
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture fixture = await FilesystemFixtureAsync(t).ConfigureAwait(false);
            try
            {
                await fixture.Service.UpsertAsync(fixture.Scope, fixture.Category, new Memory { Slug = "a", Title = "Centerline", Body = "Control the centerline." }).ConfigureAwait(false);

                string chatJson = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = "Not in memory." } } } });
                using StubResponseHandler handler = new StubResponseHandler(chatJson);
                InferenceService inference = new InferenceService(handler);
                ModelEndpoint endpoint = new ModelEndpoint { TenantId = fixture.Scope.TenantId, Name = "chat", Kind = EndpointKindEnum.Inference, ApiFormat = ApiFormatEnum.OpenAI, BaseUrl = "http://127.0.0.1:9999" };
                MemoryChatService chat = new MemoryChatService(fixture.Service, inference);
                await chat.AskAsync(fixture.Scope, endpoint, "What port does the database use?", 0).ConfigureAwait(false);

                string sent = handler.LastRequestBody ?? string.Empty;
                TestCase.Require(sent.Contains("only facts stated in the provided memories", StringComparison.Ordinal), "The prompt should restrict answers to stated facts.");
                TestCase.Require(sent.Contains("not in memory", StringComparison.Ordinal), "The prompt should tell the model to say when the answer is not in memory.");
                TestCase.Require(sent.Contains("Cite the memory behind every claim", StringComparison.Ordinal), "The prompt should require a citation for every claim.");
            }
            finally
            {
                DeleteWork(fixture.Work);
            }
        }

        private static void InstructionsUpdateNotDuplicate()
        {
            List<Instruction> instructions = DefaultInstructions.For("ten_x");
            string all = string.Join("\n", instructions.Select(i => i.Content));
            TestCase.Require(all.Contains("update the existing memory", StringComparison.Ordinal), "The default instructions should tell agents to update a changed fact in place.");
            TestCase.Require(all.Contains("Do not add a second memory for the new value", StringComparison.Ordinal), "The default instructions should warn against duplicating a changed fact.");
        }

        private static void SanitizerReplacesLoneSurrogates()
        {
            string pair = char.ConvertFromUtf32(0x1F680);
            string high = pair.Substring(0, 1);
            string low = pair.Substring(1, 1);
            string emoji = "ship it " + pair + " now";
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates(null) == null, "Null passes through.");
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates("plain text") == "plain text", "Valid text is unchanged.");
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates(emoji) == emoji, "A valid surrogate pair (emoji) is unchanged.");
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates("a" + high + "b") == "a�b", "A lone high surrogate is replaced.");
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates("a" + low + "b") == "a�b", "A lone low surrogate is replaced.");
            TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates("end" + high) == "end�", "A trailing high surrogate is replaced.");
        }

        private static async Task ChunkerEmojiSafeAsync()
        {
            string rocket = char.ConvertFromUtf32(0x1F680);
            string smile = char.ConvertFromUtf32(0x1F600);
            string body = string.Concat(Enumerable.Range(0, 400).Select(i => (i % 3 == 0 ? rocket : smile) + "launch" + i + " "));
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), body, "Emoji-heavy notes", 1.0).ConfigureAwait(false);
            TestCase.Require(chunks.Count > 1, "The emoji-heavy body should split into several chunks.");
            foreach (MemoryChunk chunk in chunks)
            {
                TestCase.Require(TextSanitizer.ReplaceInvalidSurrogates(chunk.Text) == chunk.Text, "Chunk " + chunk.Ordinal + " must not contain a split surrogate pair.");
                TestCase.Require(chunk.Text.Contains(rocket, StringComparison.Ordinal) || chunk.Text.Contains(smile, StringComparison.Ordinal), "Chunk " + chunk.Ordinal + " should keep its emoji intact.");
                if (chunk.StartOffset >= 0) TestCase.Require(body.Substring(chunk.StartOffset, chunk.EndOffset - chunk.StartOffset) == chunk.Text, "Chunk offsets must still address the original body.");
            }
        }

        private static async Task ChunkerNoRedundantTailChunksAsync()
        {
            // Short, numbered words make the fixed-token chunker emit a run of tiny trailing overlap chunks.
            string body = string.Concat(Enumerable.Range(0, 400).Select(i => "XYlaunch" + i + " "));
            IReadOnlyList<MemoryChunk> chunks = await MemoryChunker.ChunkAsync(ChunkScope(ChunkingModeEnum.OnOverflow), MiniLm(), body, null, 1.0).ConfigureAwait(false);
            TestCase.Require(chunks.Count > 1, "The body should split.");
            for (int i = 1; i < chunks.Count; i++)
            {
                TestCase.Require(!chunks[i - 1].Text.Contains(chunks[i].Text, StringComparison.Ordinal), "Chunk " + i + " is wholly contained in chunk " + (i - 1) + ".");
                TestCase.Require(chunks[i].Ordinal == i, "Ordinals should stay contiguous after dropping redundant chunks.");
            }

            TestCase.Require(body.TrimEnd().EndsWith(chunks[chunks.Count - 1].Text.TrimEnd(), StringComparison.Ordinal), "The last kept chunk should still reach the end of the body.");
        }

        private static async Task UpsertWithLoneSurrogateAsync()
        {
            string pair = char.ConvertFromUtf32(0x1F680);
            string high = pair.Substring(0, 1);
            string low = pair.Substring(1, 1);
            using TempSqlite t = await TempSqlite.CreateAsync().ConfigureAwait(false);
            FilesystemFixture fixture = await FilesystemFixtureAsync(t).ConfigureAwait(false);
            try
            {
                Memory saved = await fixture.Service.UpsertAsync(fixture.Scope, fixture.Category, new Memory { Slug = "scraped", Title = "Scraped " + high + " text", Body = "Pasted from a page " + low + " with a stray code unit." }).ConfigureAwait(false);
                TestCase.Require(saved.Body.Contains('�') && !saved.Body.Contains(low, StringComparison.Ordinal), "The stray code unit in the body should be replaced.");
                TestCase.Require(saved.Title != null && saved.Title.Contains('�'), "The stray code unit in the title should be replaced.");
            }
            finally
            {
                DeleteWork(fixture.Work);
            }
        }

        #endregion
    }
}
