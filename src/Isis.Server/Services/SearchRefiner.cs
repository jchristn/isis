namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Stores;

    /// <summary>
    /// Applies the memory-graph steps of a search after retrieval: resolves each hit to its memory row, handles
    /// superseded memories (demote, hide, or annotate), and follows links from the results.
    /// </summary>
    internal sealed class SearchRefiner
    {
        #region Private-Members

        private const int _MaxChainDepth = 8;
        private static readonly Regex _WikiLink = new Regex(@"\[\[([^\[\]\r\n|]{1,200})(?:\|[^\[\]\r\n]*)?\]\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        internal SearchRefiner(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Resolve hits to memories, then apply supersession handling and link expansion.
        /// </summary>
        internal async Task<List<MemorySearchHit>> RefineAsync(Scope scope, List<MemorySearchHit> hits, SupersededHandlingEnum handling, int linkExpansion, int topK, int snippetChars, CancellationToken token)
        {
            if (hits.Count == 0) return hits;

            Dictionary<string, Memory> byId = new Dictionary<string, Memory>(StringComparer.Ordinal);
            Dictionary<MemorySearchHit, Memory> memoryOf = await ResolveHitsAsync(scope, hits, byId, token).ConfigureAwait(false);

            List<MemorySearchHit> output = await ApplySupersessionAsync(scope, hits, memoryOf, byId, handling, snippetChars, token).ConfigureAwait(false);
            if (handling != SupersededHandlingEnum.Include && output.Count > topK) output = output.Take(topK).ToList();

            if (linkExpansion > 0) output = await ExpandLinksAsync(scope, output, memoryOf, byId, handling, linkExpansion, snippetChars, token).ConfigureAwait(false);
            return output;
        }

        /// <summary>
        /// Extract the slugs a memory links to: its <c>Links</c> list followed by <c>[[slug]]</c> references in its body.
        /// </summary>
        internal static List<string> LinkedSlugs(Memory memory)
        {
            List<string> slugs = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string link in memory.Links ?? new List<string>())
            {
                string trimmed = (link ?? string.Empty).Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed)) slugs.Add(trimmed);
            }

            foreach (Match match in _WikiLink.Matches(memory.Body ?? string.Empty))
            {
                string slug = match.Groups[1].Value.Trim();
                if (slug.Length > 0 && seen.Add(slug)) slugs.Add(slug);
            }

            slugs.RemoveAll(s => string.Equals(s, memory.Slug, StringComparison.Ordinal) || string.Equals(s, memory.Id, StringComparison.Ordinal));
            return slugs;
        }

        #endregion

        #region Private-Methods

        private async Task<Dictionary<MemorySearchHit, Memory>> ResolveHitsAsync(Scope scope, List<MemorySearchHit> hits, Dictionary<string, Memory> byId, CancellationToken token)
        {
            List<string> slugs = hits.Where(h => !string.IsNullOrEmpty(h.Slug)).Select(h => h.Slug!).Distinct(StringComparer.Ordinal).ToList();
            List<Memory> memories = await _Database.Memories.ReadBySlugsAsync(scope.TenantId, scope.Id, slugs, token).ConfigureAwait(false);
            foreach (Memory memory in memories) byId[memory.Id] = memory;

            Dictionary<MemorySearchHit, Memory> memoryOf = new Dictionary<MemorySearchHit, Memory>();
            foreach (MemorySearchHit hit in hits)
            {
                // A slug is unique per category, so disambiguate by store key (RecallDB keys documents by memory id;
                // the filesystem store by file path, which the index records as the store key).
                List<Memory> candidates = memories.Where(m => string.Equals(m.Slug, hit.Slug, StringComparison.Ordinal)).ToList();
                Memory? match = candidates.FirstOrDefault(m => string.Equals(m.StoreKey, hit.StoreKey, StringComparison.Ordinal) || string.Equals(m.Id, hit.StoreKey, StringComparison.Ordinal));
                if (match == null && candidates.Count == 1) match = candidates[0];
                if (match == null) continue;

                memoryOf[hit] = match;
                hit.MemoryId = match.Id;
            }

            return memoryOf;
        }

        private async Task<Memory?> ResolveCurrentAsync(Scope scope, Memory memory, Dictionary<string, Memory> byId, CancellationToken token)
        {
            // Follow the replacement chain (A superseded by B superseded by C) to the current memory. A dangling or
            // cyclic chain stops at the last memory that resolves.
            Memory current = memory;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { memory.Id };
            for (int depth = 0; depth < _MaxChainDepth && !string.IsNullOrEmpty(current.SupersededBy); depth++)
            {
                string nextId = current.SupersededBy!;
                if (!visited.Add(nextId)) break;

                if (!byId.TryGetValue(nextId, out Memory? next))
                {
                    next = await _Database.Memories.ReadAsync(scope.TenantId, nextId, token).ConfigureAwait(false);
                    if (next == null || !string.Equals(next.ScopeId, scope.Id, StringComparison.Ordinal)) break;
                    byId[next.Id] = next;
                }

                current = next;
            }

            return ReferenceEquals(current, memory) ? null : current;
        }

        private async Task<List<MemorySearchHit>> ApplySupersessionAsync(Scope scope, List<MemorySearchHit> hits, Dictionary<MemorySearchHit, Memory> memoryOf, Dictionary<string, Memory> byId, SupersededHandlingEnum handling, int snippetChars, CancellationToken token)
        {
            if (!hits.Any(h => memoryOf.TryGetValue(h, out Memory? m) && !string.IsNullOrEmpty(m.SupersededBy))) return hits;

            Dictionary<string, MemorySearchHit> hitByMemoryId = new Dictionary<string, MemorySearchHit>(StringComparer.Ordinal);
            foreach (KeyValuePair<MemorySearchHit, Memory> entry in memoryOf)
            {
                if (!hitByMemoryId.ContainsKey(entry.Value.Id)) hitByMemoryId[entry.Value.Id] = entry.Key;
            }

            List<MemorySearchHit> output = new List<MemorySearchHit>(hits.Count + 2);
            HashSet<string> emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (MemorySearchHit hit in hits)
            {
                string key = memoryOf.TryGetValue(hit, out Memory? memory) ? memory.Id : "#" + hit.StoreKey;
                if (emitted.Contains(key)) continue;

                Memory? replacement = memory != null && !string.IsNullOrEmpty(memory.SupersededBy)
                    ? await ResolveCurrentAsync(scope, memory, byId, token).ConfigureAwait(false)
                    : null;

                if (replacement == null)
                {
                    output.Add(hit);
                    emitted.Add(key);
                    continue;
                }

                hit.SupersededBy = replacement.Slug;
                if (handling == SupersededHandlingEnum.Include)
                {
                    output.Add(hit);
                    emitted.Add(key);
                    continue;
                }

                // Put the replacement where the stale memory ranked (taking the retrieved hit for it when it ranked
                // lower, or a hit built from the index when it was not retrieved), then the stale memory after it.
                if (!emitted.Contains(replacement.Id))
                {
                    MemorySearchHit replacementHit;
                    if (hitByMemoryId.TryGetValue(replacement.Id, out MemorySearchHit? retrieved))
                    {
                        replacementHit = retrieved;
                    }
                    else
                    {
                        replacementHit = BuildHit(replacement, hit.Score, snippetChars);
                        replacementHit.LinkedFrom = hit.Slug;
                        memoryOf[replacementHit] = replacement;
                    }

                    output.Add(replacementHit);
                    emitted.Add(replacement.Id);
                }

                if (handling == SupersededHandlingEnum.Demote) output.Add(hit);
                emitted.Add(key);
            }

            return output;
        }

        private async Task<List<MemorySearchHit>> ExpandLinksAsync(Scope scope, List<MemorySearchHit> hits, Dictionary<MemorySearchHit, Memory> memoryOf, Dictionary<string, Memory> byId, SupersededHandlingEnum handling, int linkExpansion, int snippetChars, CancellationToken token)
        {
            Dictionary<MemorySearchHit, List<string>> linksOf = new Dictionary<MemorySearchHit, List<string>>();
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (MemorySearchHit hit in hits)
            {
                if (!memoryOf.TryGetValue(hit, out Memory? memory)) continue;
                List<string> links = LinkedSlugs(memory);
                if (links.Count == 0) continue;
                linksOf[hit] = links;
                foreach (string link in links) wanted.Add(link);
            }

            if (wanted.Count == 0) return hits;

            List<string> ids = wanted.Where(w => w.StartsWith(Constants.MemoryPrefix, StringComparison.Ordinal)).ToList();
            List<Memory> linked = await _Database.Memories.ReadBySlugsAsync(scope.TenantId, scope.Id, wanted.ToList(), token).ConfigureAwait(false);
            if (ids.Count > 0)
            {
                List<Memory> byIds = await _Database.Memories.ReadManyAsync(scope.TenantId, ids, token).ConfigureAwait(false);
                linked.AddRange(byIds.Where(m => string.Equals(m.ScopeId, scope.Id, StringComparison.Ordinal)));
            }

            foreach (Memory memory in linked) byId[memory.Id] = memory;

            HashSet<string> present = new HashSet<string>(memoryOf.Where(e => hits.Contains(e.Key)).Select(e => e.Value.Id), StringComparer.Ordinal);
            List<MemorySearchHit> output = new List<MemorySearchHit>(hits.Count + linkExpansion);
            int remaining = linkExpansion;
            foreach (MemorySearchHit hit in hits)
            {
                output.Add(hit);
                if (remaining == 0 || !linksOf.TryGetValue(hit, out List<string>? links)) continue;

                Memory linker = memoryOf[hit];
                foreach (string link in links)
                {
                    if (remaining == 0) break;

                    // Prefer the linker's own category when the slug exists in several.
                    List<Memory> matches = linked.Where(m => string.Equals(m.Slug, link, StringComparison.Ordinal) || string.Equals(m.Id, link, StringComparison.Ordinal)).ToList();
                    Memory? target = matches.FirstOrDefault(m => string.Equals(m.CategoryId, linker.CategoryId, StringComparison.Ordinal)) ?? matches.FirstOrDefault();
                    if (target == null) continue;

                    if (handling != SupersededHandlingEnum.Include && !string.IsNullOrEmpty(target.SupersededBy))
                    {
                        target = await ResolveCurrentAsync(scope, target, byId, token).ConfigureAwait(false) ?? target;
                    }

                    if (!present.Add(target.Id)) continue;

                    MemorySearchHit linkedHit = BuildHit(target, hit.Score, snippetChars);
                    linkedHit.LinkedFrom = hit.Slug;
                    output.Add(linkedHit);
                    remaining--;
                }
            }

            return output;
        }

        private static MemorySearchHit BuildHit(Memory memory, double score, int snippetChars)
        {
            string body = memory.Body ?? string.Empty;
            return new MemorySearchHit
            {
                StoreKey = memory.StoreKey ?? memory.Id,
                Slug = memory.Slug,
                Title = memory.Title,
                Snippet = body.Length > snippetChars ? body.Substring(0, snippetChars) + "…" : body,
                Score = score,
                MemoryId = memory.Id
            };
        }

        #endregion
    }
}
