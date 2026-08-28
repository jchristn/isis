namespace Isis.Core.Stores.Filesystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// A memory store that persists memories as flat files at a target path, either as a hierarchy of
    /// one file per memory or as a single aggregated file. Keyword search only; no embeddings.
    /// </summary>
    public class FilesystemMemoryStore : IMemoryStore
    {
        #region Public-Members

        /// <inheritdoc />
        public StoreCapabilities Capabilities { get; } = new StoreCapabilities
        {
            SupportsKeyword = true,
            SupportsSemantic = false,
            SupportsHybrid = false,
            RequiresEmbedding = false,
            Description = "Flat-file store (single file or hierarchy). Keyword search only; git-trackable."
        };

        #endregion

        #region Private-Members

        private static readonly string _BlockOpenPrefix = "<!-- isis:memory ";
        private static readonly string _BlockClose = "<!-- /isis:memory -->";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a filesystem memory store.
        /// </summary>
        public FilesystemMemoryStore()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task EnsureScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            string root = ResolveRoot(scope);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> UpsertAsync(Scope scope, Memory memory, float[]? embedding, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            if (scope.FilesystemLayout == FilesystemLayoutEnum.SingleFile)
            {
                return await UpsertSingleFileAsync(scope, memory, token).ConfigureAwait(false);
            }

            return await UpsertHierarchyAsync(scope, memory, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Scope scope, Memory memory, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            if (scope.FilesystemLayout == FilesystemLayoutEnum.SingleFile)
            {
                string file = SingleFilePath(scope);
                if (File.Exists(file))
                {
                    List<MemoryBlock> blocks = ParseBlocks(await File.ReadAllTextAsync(file, token).ConfigureAwait(false));
                    blocks.RemoveAll(b => b.Slug == memory.Slug && b.CategoryId == memory.CategoryId);
                    await File.WriteAllTextAsync(file, RenderBlocks(blocks), token).ConfigureAwait(false);
                }
                return;
            }

            string path = HierarchyPath(scope, memory);
            if (File.Exists(path)) File.Delete(path);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteScopeAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (String.IsNullOrEmpty(scope.TargetPath)) return;

            try
            {
                if (scope.FilesystemLayout == FilesystemLayoutEnum.SingleFile)
                {
                    string file = SingleFilePath(scope);
                    if (File.Exists(file)) File.Delete(file);
                }
                else
                {
                    string root = ResolveRoot(scope);
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
            }
            catch (IOException)
            {
                // Best-effort teardown during cascade; ignore locked/missing files.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort teardown during cascade; ignore permission failures.
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<MemorySearchResult> SearchAsync(Scope scope, MemorySearchQuery query, float[]? queryEmbedding, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (query == null) throw new ArgumentNullException(nameof(query));

            MemorySearchResult result = new MemorySearchResult { EffectiveMode = SearchModeEnum.Keyword };
            if (query.Mode != SearchModeEnum.Keyword)
            {
                result.Notice = "The filesystem store supports keyword search only; the request was served as keyword search.";
            }

            List<MemoryBlock> blocks = await LoadAllBlocksAsync(scope, token).ConfigureAwait(false);
            string[] terms = query.QueryText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            List<MemorySearchHit> hits = new List<MemorySearchHit>();
            foreach (MemoryBlock block in blocks)
            {
                if (!String.IsNullOrEmpty(query.CategoryFilter) && !String.Equals(block.CategoryId, query.CategoryFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double score = ScoreBlock(block, terms);
                if (score <= 0.0 && terms.Length > 0) continue;

                hits.Add(new MemorySearchHit
                {
                    StoreKey = block.StoreKey,
                    Slug = block.Slug,
                    Title = block.Title,
                    Snippet = BuildSnippet(block.Body, terms, query.TokenBudget),
                    Score = score
                });
            }

            result.Hits = hits.OrderByDescending(h => h.Score).Take(query.TopK).ToList();
            return result;
        }

        #endregion

        #region Private-Methods

        private static string ResolveRoot(Scope scope)
        {
            if (String.IsNullOrEmpty(scope.TargetPath)) throw new InvalidOperationException("Scope '" + scope.Id + "' uses the filesystem store but has no target path configured.");
            return scope.TargetPath!;
        }

        private static string SingleFilePath(Scope scope)
        {
            return Path.Combine(ResolveRoot(scope), "isis-memory.md");
        }

        private static string HierarchyPath(Scope scope, Memory memory)
        {
            string categoryDir = SanitizeSegment(memory.CategoryId);
            return Path.Combine(ResolveRoot(scope), categoryDir, SanitizeSegment(memory.Slug) + ".md");
        }

        private async Task<string> UpsertHierarchyAsync(Scope scope, Memory memory, CancellationToken token)
        {
            string path = HierarchyPath(scope, memory);
            string? dir = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, RenderBlock(ToBlock(memory)), token).ConfigureAwait(false);
            return path;
        }

        private async Task<string> UpsertSingleFileAsync(Scope scope, Memory memory, CancellationToken token)
        {
            string file = SingleFilePath(scope);
            string? dir = Path.GetDirectoryName(file);
            if (!String.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            List<MemoryBlock> blocks = File.Exists(file)
                ? ParseBlocks(await File.ReadAllTextAsync(file, token).ConfigureAwait(false))
                : new List<MemoryBlock>();

            blocks.RemoveAll(b => b.Slug == memory.Slug && b.CategoryId == memory.CategoryId);
            blocks.Add(ToBlock(memory));

            await File.WriteAllTextAsync(file, RenderBlocks(blocks), token).ConfigureAwait(false);
            return file + "#" + memory.CategoryId + "/" + memory.Slug;
        }

        private async Task<List<MemoryBlock>> LoadAllBlocksAsync(Scope scope, CancellationToken token)
        {
            List<MemoryBlock> blocks = new List<MemoryBlock>();
            string root = ResolveRoot(scope);
            if (!Directory.Exists(root)) return blocks;

            if (scope.FilesystemLayout == FilesystemLayoutEnum.SingleFile)
            {
                string file = SingleFilePath(scope);
                if (File.Exists(file)) blocks.AddRange(ParseBlocks(await File.ReadAllTextAsync(file, token).ConfigureAwait(false)));
                return blocks;
            }

            foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                List<MemoryBlock> parsed = ParseBlocks(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
                foreach (MemoryBlock block in parsed)
                {
                    block.StoreKey = path;
                    blocks.Add(block);
                }
            }

            return blocks;
        }

        private static MemoryBlock ToBlock(Memory memory)
        {
            return new MemoryBlock
            {
                Slug = memory.Slug,
                CategoryId = memory.CategoryId,
                Title = memory.Title,
                Body = memory.Body,
                StoreKey = memory.StoreKey ?? String.Empty
            };
        }

        private static string RenderBlock(MemoryBlock block)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(_BlockOpenPrefix)
              .Append("slug=\"").Append(block.Slug).Append("\" ")
              .Append("category=\"").Append(block.CategoryId).Append("\" -->\n");
            if (!String.IsNullOrEmpty(block.Title)) sb.Append("# ").Append(block.Title).Append("\n\n");
            sb.Append(block.Body).Append('\n');
            sb.Append(_BlockClose).Append('\n');
            return sb.ToString();
        }

        private static string RenderBlocks(IEnumerable<MemoryBlock> blocks)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MemoryBlock block in blocks)
            {
                sb.Append(RenderBlock(block)).Append('\n');
            }

            return sb.ToString();
        }

        private static List<MemoryBlock> ParseBlocks(string content)
        {
            List<MemoryBlock> blocks = new List<MemoryBlock>();
            if (String.IsNullOrEmpty(content)) return blocks;

            int cursor = 0;
            while (true)
            {
                int open = content.IndexOf(_BlockOpenPrefix, cursor, StringComparison.Ordinal);
                if (open < 0) break;
                int headerEnd = content.IndexOf("-->", open, StringComparison.Ordinal);
                if (headerEnd < 0) break;
                int close = content.IndexOf(_BlockClose, headerEnd, StringComparison.Ordinal);
                if (close < 0) break;

                string header = content.Substring(open, headerEnd - open);
                string inner = content.Substring(headerEnd + 3, close - (headerEnd + 3)).Trim('\n', '\r');

                MemoryBlock block = new MemoryBlock
                {
                    Slug = ExtractAttribute(header, "slug"),
                    CategoryId = ExtractAttribute(header, "category")
                };

                if (inner.StartsWith("# ", StringComparison.Ordinal))
                {
                    int newline = inner.IndexOf('\n');
                    if (newline > 0)
                    {
                        block.Title = inner.Substring(2, newline - 2).Trim();
                        block.Body = inner.Substring(newline + 1).Trim('\n', '\r');
                    }
                    else
                    {
                        block.Title = inner.Substring(2).Trim();
                        block.Body = String.Empty;
                    }
                }
                else
                {
                    block.Body = inner;
                }

                blocks.Add(block);
                cursor = close + _BlockClose.Length;
            }

            return blocks;
        }

        private static string ExtractAttribute(string header, string name)
        {
            string marker = name + "=\"";
            int start = header.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return String.Empty;
            start += marker.Length;
            int end = header.IndexOf('"', start);
            if (end < 0) return String.Empty;
            return header.Substring(start, end - start);
        }

        private static double ScoreBlock(MemoryBlock block, string[] terms)
        {
            if (terms.Length == 0) return 1.0;
            string haystack = ((block.Title ?? String.Empty) + "\n" + block.Body).ToLowerInvariant();
            double score = 0.0;
            foreach (string term in terms)
            {
                string needle = term.ToLowerInvariant();
                int index = 0;
                while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
                {
                    score += 1.0;
                    index += needle.Length;
                }
            }

            return score;
        }

        private static string BuildSnippet(string body, string[] terms, int? tokenBudget)
        {
            int window = tokenBudget.HasValue && tokenBudget.Value > 0 ? tokenBudget.Value : 240;
            if (String.IsNullOrEmpty(body)) return String.Empty;

            int anchor = 0;
            if (terms.Length > 0)
            {
                int found = body.ToLowerInvariant().IndexOf(terms[0].ToLowerInvariant(), StringComparison.Ordinal);
                if (found > 0) anchor = Math.Max(0, found - window / 4);
            }

            int length = Math.Min(window, body.Length - anchor);
            string snippet = body.Substring(anchor, length).Trim();
            if (anchor > 0) snippet = "…" + snippet;
            if (anchor + length < body.Length) snippet += "…";
            return snippet;
        }

        private static string SanitizeSegment(string value)
        {
            if (String.IsNullOrEmpty(value)) return "_";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return sb.ToString();
        }

        #endregion
    }
}
