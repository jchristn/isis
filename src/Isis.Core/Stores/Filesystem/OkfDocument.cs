namespace Isis.Core.Stores.Filesystem
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Serializes and parses a single Open Knowledge Format (OKF) concept document: a markdown file with a
    /// YAML frontmatter block. The frontmatter carries OKF's core fields (<c>type</c>, <c>title</c>,
    /// <c>description</c>, <c>resource</c>, <c>tags</c>, <c>timestamp</c>) plus Isis provenance extras
    /// (<c>slug</c>, <c>category</c>, <c>links</c>, <c>author</c>, <c>sessionId</c>, <c>model</c>,
    /// <c>version</c>, <c>salience</c>, <c>created</c>, <c>metadata</c>). The markdown body is preserved
    /// verbatim. The reader is deliberately tolerant — it accepts foreign bundles that omit the Isis extras,
    /// use bare (unquoted) scalars, and use either flow (<c>[a, b]</c>) or block (<c>- a</c>) lists — so a
    /// bundle produced by another OKF tool round-trips into Isis without a translation layer.
    /// </summary>
    public static class OkfDocument
    {
        #region Public-Members

        /// <summary>
        /// The reserved bundle index filename (navigation, not a memory).
        /// </summary>
        public const string IndexFileName = "index.md";

        /// <summary>
        /// The reserved change-log filename (navigation, not a memory).
        /// </summary>
        public const string LogFileName = "log.md";

        #endregion

        #region Private-Members

        private static readonly string _Delimiter = "---";

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the given filename is an OKF reserved file (index or log) that must not be treated as a
        /// memory when reading a bundle.
        /// </summary>
        /// <param name="fileName">A bare filename (no directory).</param>
        /// <returns>True when the file is reserved.</returns>
        public static bool IsReservedFileName(string fileName)
        {
            if (String.IsNullOrEmpty(fileName)) return false;
            return String.Equals(fileName, IndexFileName, StringComparison.OrdinalIgnoreCase)
                || String.Equals(fileName, LogFileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Render a memory as an OKF concept document (YAML frontmatter + markdown body).
        /// </summary>
        /// <param name="memory">The memory to serialize.</param>
        /// <returns>The document text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when memory is null.</exception>
        public static string Serialize(Memory memory)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));

            StringBuilder sb = new StringBuilder();
            sb.Append(_Delimiter).Append('\n');

            // OKF core fields.
            AppendScalar(sb, "type", memory.Type.ToString());
            AppendScalarIfSet(sb, "title", memory.Title);
            AppendScalarIfSet(sb, "description", memory.Summary);
            AppendScalarIfSet(sb, "resource", memory.Resource);
            AppendList(sb, "tags", memory.Tags);
            AppendScalar(sb, "timestamp", memory.LastUpdateUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

            // Isis provenance extras (ignored by OKF consumers; carried for lossless round-trip).
            AppendScalar(sb, "created", memory.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendScalar(sb, "slug", memory.Slug);
            AppendScalar(sb, "category", memory.CategoryId);
            AppendList(sb, "links", memory.Links);
            AppendScalarIfSet(sb, "author", memory.Author);
            AppendScalarIfSet(sb, "sessionId", memory.SessionId);
            AppendScalarIfSet(sb, "model", memory.Model);
            AppendScalar(sb, "version", memory.Version.ToString(CultureInfo.InvariantCulture));
            AppendScalar(sb, "salience", memory.Salience.ToString(CultureInfo.InvariantCulture));
            if (memory.Metadata != null && memory.Metadata.Count > 0)
            {
                AppendRaw(sb, "metadata", JsonSerializer.Serialize(memory.Metadata));
            }

            sb.Append(_Delimiter).Append('\n');
            sb.Append(memory.Body);
            if (memory.Body.Length == 0 || memory.Body[memory.Body.Length - 1] != '\n') sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Parse an OKF concept document into a memory. Tenant and scope are bundle context and are not
        /// carried in the file, so they are left unset for the caller to assign. Missing or malformed fields
        /// fall back to sensible defaults rather than throwing.
        /// </summary>
        /// <param name="content">The document text.</param>
        /// <param name="slugFallback">Slug to use when the frontmatter omits one (typically the filename).</param>
        /// <param name="categoryFallback">Category to use when the frontmatter omits one (typically the parent directory).</param>
        /// <returns>The parsed memory.</returns>
        public static Memory Parse(string content, string slugFallback, string categoryFallback)
        {
            Dictionary<string, string> scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string body = SplitFrontmatter(content ?? String.Empty, scalars, lists);

            string slug = GetScalar(scalars, "slug");
            if (String.IsNullOrEmpty(slug)) slug = String.IsNullOrEmpty(slugFallback) ? "memory" : slugFallback;
            string category = GetScalar(scalars, "category");
            if (String.IsNullOrEmpty(category)) category = String.IsNullOrEmpty(categoryFallback) ? "_" : categoryFallback;

            Memory memory = new Memory
            {
                Slug = slug,
                CategoryId = category,
                Title = NullIfEmpty(GetScalar(scalars, "title")),
                Summary = NullIfEmpty(GetScalar(scalars, "description")),
                Resource = NullIfEmpty(GetScalar(scalars, "resource")),
                Author = NullIfEmpty(GetScalar(scalars, "author")),
                SessionId = NullIfEmpty(GetScalar(scalars, "sessionId")),
                Model = NullIfEmpty(GetScalar(scalars, "model")),
                Body = body
            };

            if (lists.TryGetValue("tags", out List<string>? tags)) memory.Tags = tags;
            if (lists.TryGetValue("links", out List<string>? links)) memory.Links = links;

            string typeRaw = GetScalar(scalars, "type");
            if (Enum.TryParse(typeRaw, true, out MemoryTypeEnum parsedType))
            {
                memory.Type = parsedType;
            }
            else
            {
                memory.Type = MemoryTypeEnum.Reference;
                if (!String.IsNullOrEmpty(typeRaw)) memory.Metadata["sourceType"] = typeRaw;
            }

            int version;
            if (Int32.TryParse(GetScalar(scalars, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version) && version > 0)
            {
                memory.Version = version;
            }

            double salience;
            if (Double.TryParse(GetScalar(scalars, "salience"), NumberStyles.Float, CultureInfo.InvariantCulture, out salience))
            {
                memory.Salience = salience;
            }

            DateTime? created = ParseDate(GetScalar(scalars, "created"));
            DateTime? timestamp = ParseDate(GetScalar(scalars, "timestamp"));
            memory.CreatedUtc = created ?? timestamp ?? DateTime.UtcNow;
            memory.LastUpdateUtc = timestamp ?? memory.CreatedUtc;

            string metadataRaw = GetScalar(scalars, "metadata");
            if (!String.IsNullOrEmpty(metadataRaw))
            {
                try
                {
                    Dictionary<string, string>? map = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataRaw);
                    if (map != null)
                    {
                        foreach (KeyValuePair<string, string> pair in map) memory.Metadata[pair.Key] = pair.Value;
                    }
                }
                catch (JsonException)
                {
                    // Ignore a malformed metadata block rather than failing the whole parse.
                }
            }

            return memory;
        }

        #endregion

        #region Private-Methods-Serialize

        private static void AppendScalar(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append(": ").Append(QuoteScalar(value)).Append('\n');
        }

        private static void AppendScalarIfSet(StringBuilder sb, string key, string? value)
        {
            if (String.IsNullOrEmpty(value)) return;
            AppendScalar(sb, key, value!);
        }

        private static void AppendRaw(StringBuilder sb, string key, string rawValue)
        {
            sb.Append(key).Append(": ").Append(rawValue).Append('\n');
        }

        private static void AppendList(StringBuilder sb, string key, List<string> values)
        {
            if (values == null || values.Count == 0) return;
            sb.Append(key).Append(": [");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(QuoteScalar(values[i]));
            }
            sb.Append("]\n");
        }

        private static string QuoteScalar(string value)
        {
            if (value == null) return "\"\"";
            string collapsed = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            string escaped = collapsed.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }

        #endregion

        #region Private-Methods-Parse

        private static string SplitFrontmatter(string content, Dictionary<string, string> scalars, Dictionary<string, List<string>> lists)
        {
            string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
            if (!normalized.StartsWith(_Delimiter + "\n", StringComparison.Ordinal) && normalized != _Delimiter)
            {
                // No frontmatter — the whole content is the body.
                return content;
            }

            int firstBreak = normalized.IndexOf('\n');
            if (firstBreak < 0) return String.Empty;

            int closeIndex = normalized.IndexOf("\n" + _Delimiter, firstBreak, StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                // Unterminated frontmatter — treat everything after the opener as body.
                return normalized.Substring(firstBreak + 1);
            }

            string frontmatter = normalized.Substring(firstBreak + 1, closeIndex - (firstBreak + 1));
            int afterClose = closeIndex + 1 + _Delimiter.Length;
            string body = afterClose <= normalized.Length ? normalized.Substring(Math.Min(afterClose, normalized.Length)) : String.Empty;
            body = body.TrimStart('\n');
            body = body.TrimEnd('\n');

            ParseFrontmatter(frontmatter, scalars, lists);
            return body;
        }

        private static void ParseFrontmatter(string frontmatter, Dictionary<string, string> scalars, Dictionary<string, List<string>> lists)
        {
            string[] lines = frontmatter.Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (String.IsNullOrWhiteSpace(line)) { i++; continue; }

                int colon = line.IndexOf(':');
                if (colon < 0) { i++; continue; }

                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (String.IsNullOrEmpty(key)) { i++; continue; }

                if (value.Length == 0)
                {
                    // Possible block-style list on the following indented "- item" lines.
                    List<string> items = new List<string>();
                    int j = i + 1;
                    while (j < lines.Length)
                    {
                        string next = lines[j].Trim();
                        if (next.StartsWith("- ", StringComparison.Ordinal))
                        {
                            items.Add(Unquote(next.Substring(2).Trim()));
                            j++;
                        }
                        else if (next.Length == 0)
                        {
                            j++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (items.Count > 0) { lists[key] = items; i = j; continue; }
                    scalars[key] = String.Empty;
                    i++;
                    continue;
                }

                if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                {
                    lists[key] = ParseFlowList(value);
                    i++;
                    continue;
                }

                if (value.StartsWith("{", StringComparison.Ordinal))
                {
                    // Raw JSON (metadata) — keep verbatim, do not unquote.
                    scalars[key] = value;
                    i++;
                    continue;
                }

                scalars[key] = Unquote(value);
                i++;
            }
        }

        private static List<string> ParseFlowList(string value)
        {
            string inner = value.Substring(1, value.Length - 2).Trim();
            List<string> items = new List<string>();
            if (inner.Length == 0) return items;

            foreach (string part in inner.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0) continue;
                items.Add(Unquote(trimmed));
            }

            return items;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2)
            {
                if ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\''))
                {
                    string inner = value.Substring(1, value.Length - 2);
                    if (value[0] == '"') inner = inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
                    return inner;
                }
            }

            return value;
        }

        private static string GetScalar(Dictionary<string, string> scalars, string key)
        {
            return scalars.TryGetValue(key, out string? value) ? value : String.Empty;
        }

        private static string? NullIfEmpty(string value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        private static DateTime? ParseDate(string value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            return null;
        }

        #endregion
    }
}
