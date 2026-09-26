namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Builds a round-over-round overview from saved reports: for each dataset, a metric for every question type in
    /// every round, the net change from the first round to the last, and the dataset's published baselines with the net
    /// difference; plus chat metrics per round.
    /// </summary>
    public static class HistoryWriter
    {
        #region Public-Methods

        /// <summary>
        /// Parse a round list such as "R1=20260924-17,R2=final,R3=r3".
        /// </summary>
        /// <param name="spec">The list.</param>
        /// <returns>The rounds, in order.</returns>
        /// <exception cref="ArgumentException">Thrown when an entry is not NAME=SELECTOR.</exception>
        public static List<RoundSelector> ParseRounds(string spec)
        {
            List<RoundSelector> rounds = new List<RoundSelector>();
            foreach (string part in (spec ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1) throw new ArgumentException("Round '" + part + "' must be NAME=LABEL or NAME=TIMESTAMP-PREFIX.");
                rounds.Add(new RoundSelector { Name = part.Substring(0, eq).Trim(), Selector = part.Substring(eq + 1).Trim() });
            }

            return rounds;
        }

        /// <summary>
        /// Find the newest report of a kind for a dataset and round.
        /// </summary>
        /// <param name="directory">The results directory.</param>
        /// <param name="kind">"retrieval" or "chat".</param>
        /// <param name="dataset">The dataset name.</param>
        /// <param name="selector">The round selector.</param>
        /// <returns>The report path, or null.</returns>
        public static string? FindReport(string directory, string kind, string dataset, string selector)
        {
            if (!Directory.Exists(directory)) return null;
            string datasetPart = "-" + kind + "-" + BenchmarkContext.Sanitize(dataset);
            string label = BenchmarkContext.Sanitize(selector);
            return Directory.GetFiles(directory, "*.json")
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Select(name => name!)
                .Where(name =>
                    name.EndsWith(datasetPart + "-" + label + ".json", StringComparison.OrdinalIgnoreCase)
                    || (name.StartsWith(selector, StringComparison.Ordinal) && name.EndsWith(datasetPart + ".json", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => Path.Combine(directory, name))
                .LastOrDefault();
        }

        /// <summary>
        /// Render the history report.
        /// </summary>
        /// <param name="directory">The results directory.</param>
        /// <param name="rounds">Retrieval rounds, in order.</param>
        /// <param name="datasets">Datasets to include.</param>
        /// <param name="mode">Search mode to report.</param>
        /// <param name="metric">Metric to report.</param>
        /// <param name="catalog">Published baselines.</param>
        /// <param name="chatRounds">Chat rounds, in order (may be empty).</param>
        /// <param name="chatDataset">Dataset the chat runs used.</param>
        /// <returns>Markdown.</returns>
        public static string Render(string directory, List<RoundSelector> rounds, List<string> datasets, string mode, string metric, BaselineCatalog catalog, List<RoundSelector> chatRounds, string chatDataset)
        {
            StringBuilder md = new StringBuilder();
            md.Append("# Benchmark history\n\n");
            md.Append(mode).Append(" ").Append(metric).Append(" by round. \"Net\" is the last round minus the first; n is the number of questions of that type in the last round.\n\n");

            foreach (string dataset in datasets)
            {
                List<ModeSummary?> summaries = new List<ModeSummary?>();
                List<string> sources = new List<string>();
                foreach (RoundSelector round in rounds)
                {
                    string? path = FindReport(directory, "retrieval", dataset, round.Selector);
                    RetrievalReport? report = path != null ? JsonSerializer.Deserialize<RetrievalReport>(File.ReadAllText(path), DatasetStore.Json) : null;
                    summaries.Add(report?.Modes.FirstOrDefault(m => string.Equals(m.Mode, mode, StringComparison.OrdinalIgnoreCase)));
                    sources.Add(path != null ? Path.GetFileName(path) : "(none)");
                }

                md.Append("## ").Append(dataset).Append("\n\n");
                md.Append("| Category | n | ").Append(string.Join(" | ", rounds.Select(r => r.Name))).Append(" | Net ").Append(rounds.First().Name).Append("→").Append(rounds.Last().Name).Append(" |\n");
                md.Append("|---|---|").Append(string.Concat(Enumerable.Repeat("---|", rounds.Count + 1))).Append("\n");

                ModeSummary? last = summaries.LastOrDefault(s => s != null);
                AppendRow(md, "**Overall**", last != null ? last.Queries : 0, summaries.Select(s => s != null && s.Metrics.TryGetValue(metric, out double v) ? v : (double?)null).ToList());
                List<string> types = summaries.Where(s => s != null).SelectMany(s => s!.ByType.Keys).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
                if (types.Count > 1)
                {
                    foreach (string type in types)
                    {
                        int count = last != null && last.ByType.TryGetValue(type, out Dictionary<string, double>? lt) && lt.TryGetValue("count", out double c) ? (int)c : 0;
                        AppendRow(md, type, count, summaries.Select(s => s != null && s.ByType.TryGetValue(type, out Dictionary<string, double>? m) && m.TryGetValue(metric, out double v) ? v : (double?)null).ToList());
                    }
                }

                AppendBaselines(md, catalog.For(dataset), summaries, rounds, metric, mode);
                md.Append("\nReports: ").Append(string.Join(", ", rounds.Select((r, i) => r.Name + " `" + sources[i] + "`"))).Append("\n\n");
            }

            if (chatRounds.Count > 0) AppendChat(md, directory, chatRounds, chatDataset);
            return md.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendRow(StringBuilder md, string label, int count, List<double?> values)
        {
            md.Append("| ").Append(label).Append(" | ").Append(count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "").Append(" | ");
            md.Append(string.Join(" | ", values.Select(v => v.HasValue ? v.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-")));
            double? first = values.FirstOrDefault(v => v.HasValue);
            double? final = values.LastOrDefault(v => v.HasValue);
            md.Append(" | ").Append(first.HasValue && final.HasValue ? Signed(final.Value - first.Value) : "-").Append(" |\n");
        }

        private static void AppendBaselines(StringBuilder md, DatasetBaselines? entry, List<ModeSummary?> summaries, List<RoundSelector> rounds, string metric, string mode)
        {
            if (entry == null) return;
            List<PublishedBaseline> applicable = entry.Baselines.Where(b => string.Equals(b.Metric, metric, StringComparison.OrdinalIgnoreCase) && b.CompareModes.Contains(mode, StringComparer.OrdinalIgnoreCase)).ToList();
            if (applicable.Count == 0)
            {
                if (!string.IsNullOrEmpty(entry.Note)) md.Append("\nPublished baseline: none. ").Append(entry.Note).Append("\n");
                return;
            }

            int lastIndex = summaries.FindLastIndex(s => s != null);
            double? isis = lastIndex >= 0 && summaries[lastIndex]!.Metrics.TryGetValue(metric, out double v) ? v : (double?)null;
            md.Append("\n| Published baseline | ").Append(metric).Append(" | Isis ").Append(mode).Append(" (").Append(lastIndex >= 0 ? rounds[lastIndex].Name : "-").Append(") | Net vs baseline | Source |\n|---|---|---|---|---|\n");
            foreach (PublishedBaseline baseline in applicable)
            {
                md.Append("| ").Append(baseline.System).Append(" | ").Append(baseline.Value.ToString("0.000", CultureInfo.InvariantCulture)).Append(" | ");
                md.Append(isis.HasValue ? isis.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-").Append(" | ");
                md.Append(isis.HasValue ? Signed(isis.Value - baseline.Value) : "-").Append(" | ");
                md.Append(string.IsNullOrEmpty(baseline.Url) ? baseline.Source : "[" + baseline.Source + "](" + baseline.Url + ")").Append(" |\n");
            }
        }

        private static void AppendChat(StringBuilder md, string directory, List<RoundSelector> rounds, string dataset)
        {
            string[] metrics = new string[] { "accuracy", "abstentionAccuracy", "contextRecall", "citationRecall" };
            string[] labels = new string[] { "Answer accuracy", "Correct declines on unanswerable questions", "Evidence reached the prompt", "Citation recall" };
            List<ChatReport?> reports = rounds.Select(r => FindReport(directory, "chat", dataset, r.Selector)).Select(p => p != null ? JsonSerializer.Deserialize<ChatReport>(File.ReadAllText(p), DatasetStore.Json) : null).ToList();

            md.Append("## Chat (").Append(dataset).Append(")\n\n");
            md.Append("| Metric | ").Append(string.Join(" | ", rounds.Select(r => r.Name))).Append(" | Net ").Append(rounds.First().Name).Append("→").Append(rounds.Last().Name).Append(" |\n");
            md.Append("|---|").Append(string.Concat(Enumerable.Repeat("---|", rounds.Count + 1))).Append("\n");
            for (int i = 0; i < metrics.Length; i++)
            {
                List<double?> values = reports.Select(r => r != null && r.Summary.TryGetValue(metrics[i], out double v) ? v : (double?)null).ToList();
                md.Append("| ").Append(labels[i]).Append(" | ").Append(string.Join(" | ", values.Select(v => v.HasValue ? v.Value.ToString("0.000", CultureInfo.InvariantCulture) : "-")));
                double? first = values.FirstOrDefault(v => v.HasValue);
                double? final = values.LastOrDefault(v => v.HasValue);
                md.Append(" | ").Append(first.HasValue && final.HasValue ? Signed(final.Value - first.Value) : "-").Append(" |\n");
            }

            md.Append("\n");
        }

        private static string Signed(double value)
        {
            return (value >= 0 ? "+" : "") + value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
