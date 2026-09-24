namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Test.Benchmark.Agent;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Metrics;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Writes benchmark reports as JSON (machine-readable, used by compare) and Markdown (for people).
    /// </summary>
    public static class ReportWriter
    {
        #region Public-Methods

        /// <summary>
        /// Write any report object as JSON.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <param name="basePath">Destination path without extension.</param>
        /// <returns>The JSON path.</returns>
        public static string WriteJson(object report, string basePath)
        {
            string path = basePath + ".json";
            File.WriteAllText(path, JsonSerializer.Serialize(report, report.GetType(), DatasetStore.Json), new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Write a Markdown file.
        /// </summary>
        /// <param name="markdown">The content.</param>
        /// <param name="basePath">Destination path without extension.</param>
        /// <returns>The Markdown path.</returns>
        public static string WriteMarkdown(string markdown, string basePath)
        {
            string path = basePath + ".md";
            File.WriteAllText(path, markdown, new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Render a retrieval report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string RenderRetrieval(RetrievalReport report)
        {
            StringBuilder md = new StringBuilder();
            md.Append("# Retrieval benchmark: ").Append(report.Dataset).Append("\n\n");
            md.Append(report.Description).Append("\n\n");
            AppendEnvironment(md, report.Environment, report.Config);

            md.Append("## Ingest\n\n");
            md.Append("| Documents | Reused scopes | Failures | Wall (s) | Docs/s | Concurrency | Upsert p50 | Upsert p95 | Upsert p99 |\n");
            md.Append("|---|---|---|---|---|---|---|---|---|\n");
            md.Append("| ").Append(report.Ingest.Documents).Append(" | ").Append(report.Ingest.ReusedScopes).Append(" | ").Append(report.Ingest.Failures)
              .Append(" | ").Append(report.Ingest.WallSeconds).Append(" | ").Append(report.Ingest.DocumentsPerSecond).Append(" | ").Append(report.Ingest.Concurrency)
              .Append(" | ").Append(report.Ingest.Latency.P50).Append(" ms | ").Append(report.Ingest.Latency.P95).Append(" ms | ").Append(report.Ingest.Latency.P99).Append(" ms |\n\n");
            AppendStages(md, report.Ingest.Stages);
            foreach (string error in report.Ingest.SampleErrors) md.Append("- ingest error: `").Append(error.Replace("`", "'")).Append("`\n");

            md.Append("## Accuracy by mode\n\n");
            md.Append("| Mode | Queries | ").Append(string.Join(" | ", RetrievalRunner.MetricNames)).Append(" | Errors |\n");
            md.Append("|---|---|").Append(string.Concat(Enumerable.Repeat("---|", RetrievalRunner.MetricNames.Length))).Append("---|\n");
            foreach (ModeSummary mode in report.Modes)
            {
                md.Append("| ").Append(mode.Mode).Append(" | ").Append(mode.Queries).Append(" | ");
                md.Append(string.Join(" | ", RetrievalRunner.MetricNames.Select(n => Value(mode.Metrics, n))));
                md.Append(" | ").Append(mode.Errors).Append(" |\n");
            }

            md.Append("\n## Latency by mode (client, ms)\n\n");
            md.Append("| Mode | p50 | p90 | p95 | p99 | max | mean | Mode mismatches |\n|---|---|---|---|---|---|---|---|\n");
            foreach (ModeSummary mode in report.Modes)
            {
                md.Append("| ").Append(mode.Mode).Append(" | ").Append(mode.Latency.P50).Append(" | ").Append(mode.Latency.P90).Append(" | ").Append(mode.Latency.P95)
                  .Append(" | ").Append(mode.Latency.P99).Append(" | ").Append(mode.Latency.Max).Append(" | ").Append(mode.Latency.Mean).Append(" | ").Append(mode.ModeMismatches).Append(" |\n");
            }

            md.Append("\n## Server stage breakdown by mode (mean ms per call)\n\n");
            HashSet<string> stageNames = new HashSet<string>(report.Modes.SelectMany(m => m.Stages.Keys));
            if (stageNames.Count == 0)
            {
                md.Append("_No metrics endpoint available._\n");
            }
            else
            {
                List<string> ordered = stageNames.OrderBy(StageOrder).ToList();
                md.Append("| Mode | ").Append(string.Join(" | ", ordered)).Append(" |\n|---|").Append(string.Concat(Enumerable.Repeat("---|", ordered.Count))).Append("\n");
                foreach (ModeSummary mode in report.Modes)
                {
                    md.Append("| ").Append(mode.Mode).Append(" | ");
                    md.Append(string.Join(" | ", ordered.Select(s => mode.Stages.TryGetValue(s, out StageBreakdown? b) ? b.MeanMs + " (×" + b.Count + ")" : "-")));
                    md.Append(" |\n");
                }
            }

            md.Append("\n## Score separation (can a score threshold detect \"nothing relevant\"?)\n\n");
            md.Append("| Mode | Mean top score, answerable | Mean top score, unanswerable | Score AUROC | Vector-score AUROC | Unanswerable queries |\n|---|---|---|---|---|---|\n");
            foreach (ModeSummary mode in report.Modes)
            {
                md.Append("| ").Append(mode.Mode).Append(" | ").Append(mode.MeanTopScoreAnswerable).Append(" | ").Append(mode.MeanTopScoreNegative)
                  .Append(" | ").Append(mode.ScoreAuroc.HasValue ? mode.ScoreAuroc.Value.ToString("F3") : "n/a")
                  .Append(" | ").Append(mode.VectorScoreAuroc.HasValue ? mode.VectorScoreAuroc.Value.ToString("F3") : "n/a")
                  .Append(" | ").Append(mode.NegativeQueries).Append(" |\n");
            }

            md.Append("\n## Accuracy by query type\n\n");
            HashSet<string> types = new HashSet<string>(report.Modes.SelectMany(m => m.ByType.Keys));
            string[] typeMetrics = new string[] { "hit@1", "recall@5", "all@10", "mrr@10", "ndcg@10" };
            md.Append("| Type | Mode | n | ").Append(string.Join(" | ", typeMetrics)).Append(" |\n|---|---|---|").Append(string.Concat(Enumerable.Repeat("---|", typeMetrics.Length))).Append("\n");
            foreach (string type in types.OrderBy(t => t, StringComparer.Ordinal))
            {
                foreach (ModeSummary mode in report.Modes)
                {
                    if (!mode.ByType.TryGetValue(type, out Dictionary<string, double>? metrics)) continue;
                    md.Append("| ").Append(type).Append(" | ").Append(mode.Mode).Append(" | ").Append(metrics["count"]).Append(" | ");
                    md.Append(string.Join(" | ", typeMetrics.Select(n => Value(metrics, n)))).Append(" |\n");
                }
            }

            AppendMisses(md, report);
            return md.ToString();
        }

        /// <summary>
        /// Render a load report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string RenderLoad(LoadReport report)
        {
            StringBuilder md = new StringBuilder();
            md.Append("# Load benchmark: ").Append(report.Scenario).Append(" (").Append(report.CorpusSize).Append(" memories)\n\n");
            AppendEnvironment(md, report.Environment, report.Config);

            md.Append("## Preload\n\n");
            md.Append("| Documents | Reused | Failures | Wall (s) | Docs/s | Upsert p50 | Upsert p95 |\n|---|---|---|---|---|---|---|\n");
            md.Append("| ").Append(report.Ingest.Documents).Append(" | ").Append(report.Ingest.ReusedScopes).Append(" | ").Append(report.Ingest.Failures).Append(" | ")
              .Append(report.Ingest.WallSeconds).Append(" | ").Append(report.Ingest.DocumentsPerSecond).Append(" | ").Append(report.Ingest.Latency.P50).Append(" ms | ").Append(report.Ingest.Latency.P95).Append(" ms |\n\n");

            md.Append("## Throughput and latency by concurrency (client, ms)\n\n");
            md.Append("| Concurrency | Ops/s | Error rate | Operation | Count | Ops/s | p50 | p95 | p99 | max |\n|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (LoadLevel level in report.Levels)
            {
                bool first = true;
                foreach (KeyValuePair<string, LoadOpStats> op in level.Operations)
                {
                    md.Append("| ").Append(first ? level.Concurrency.ToString() : string.Empty).Append(" | ").Append(first ? level.Throughput.ToString("F1") : string.Empty)
                      .Append(" | ").Append(first ? (level.ErrorRate * 100).ToString("F2") + "%" : string.Empty).Append(" | ").Append(op.Key).Append(" | ").Append(op.Value.Count)
                      .Append(" | ").Append(op.Value.Throughput.ToString("F1")).Append(" | ").Append(op.Value.Latency.P50).Append(" | ").Append(op.Value.Latency.P95)
                      .Append(" | ").Append(op.Value.Latency.P99).Append(" | ").Append(op.Value.Latency.Max).Append(" |\n");
                    first = false;
                }
            }

            md.Append("\n## Server stage breakdown by concurrency (mean ms per call)\n\n");
            HashSet<string> stageNames = new HashSet<string>(report.Levels.SelectMany(l => l.Stages.Keys));
            List<string> ordered = stageNames.OrderBy(StageOrder).ToList();
            if (ordered.Count > 0)
            {
                md.Append("| Concurrency | ").Append(string.Join(" | ", ordered)).Append(" |\n|---|").Append(string.Concat(Enumerable.Repeat("---|", ordered.Count))).Append("\n");
                foreach (LoadLevel level in report.Levels)
                {
                    md.Append("| ").Append(level.Concurrency).Append(" | ");
                    md.Append(string.Join(" | ", ordered.Select(s => level.Stages.TryGetValue(s, out StageBreakdown? b) ? b.MeanMs.ToString() : "-")));
                    md.Append(" |\n");
                }
            }

            foreach (LoadLevel level in report.Levels)
            {
                foreach (string error in level.SampleErrors) md.Append("- c=").Append(level.Concurrency).Append(" error: `").Append(error.Replace("`", "'").Replace("\n", " ")).Append("`\n");
            }

            return md.ToString();
        }

        /// <summary>
        /// Render a chat report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string RenderChat(ChatReport report)
        {
            StringBuilder md = new StringBuilder();
            md.Append("# Chat-with-memory benchmark: ").Append(report.Dataset).Append("\n\n");
            AppendEnvironment(md, report.Environment, report.Config);

            string[] headline = new string[] { "questions", "answerable", "unanswerable", "errors", "unjudged", "accuracy", "abstentionAccuracy", "overallAccuracy",
                "contextRecall", "accuracyWhenEvidenceRetrieved", "accuracyWhenEvidenceMissed", "citationRate", "citationPrecision", "citationRecall" };
            md.Append("## Summary\n\n| Metric | Value |\n|---|---|\n");
            foreach (string name in headline)
            {
                if (report.Summary.TryGetValue(name, out double value)) md.Append("| ").Append(name).Append(" | ").Append(value % 1 == 0 ? value.ToString("F0") : value.ToString("F3")).Append(" |\n");
            }

            md.Append("\n| Latency (client, ms) | p50 | p90 | p95 | max | mean |\n|---|---|---|---|---|---|\n");
            md.Append("| chat | ").Append(report.Latency.P50).Append(" | ").Append(report.Latency.P90).Append(" | ").Append(report.Latency.P95).Append(" | ").Append(report.Latency.Max).Append(" | ").Append(report.Latency.Mean).Append(" |\n\n");
            AppendStages(md, report.Stages);

            md.Append("## By question type\n\n| Type | n | accuracy | abstention | context recall | citation recall |\n|---|---|---|---|---|---|\n");
            foreach (KeyValuePair<string, Dictionary<string, double>> type in report.ByType)
            {
                md.Append("| ").Append(type.Key).Append(" | ").Append(type.Value["questions"]).Append(" | ").Append(Value(type.Value, "accuracy")).Append(" | ")
                  .Append(Value(type.Value, "abstentionAccuracy")).Append(" | ").Append(Value(type.Value, "contextRecall")).Append(" | ").Append(Value(type.Value, "citationRecall")).Append(" |\n");
            }

            md.Append("\n## Wrong answers (first 25)\n\n");
            foreach (ChatItem item in report.Items.Where(i => i.Correct == false).Take(25))
            {
                string answer = item.Answer.Replace("\n", " ");
                if (answer.Length > 220) answer = answer.Substring(0, 220) + "…";
                md.Append("- `").Append(item.Corpus).Append("/").Append(item.QueryId).Append("` (").Append(item.Type).Append(") evidence retrieved: ")
                  .Append(item.Retrieved.Intersect(item.Relevant).Any() ? "yes" : (item.Relevant.Count == 0 ? "n/a" : "no")).Append("\n  - Q: ").Append(item.Question.Replace("\n", " "))
                  .Append("\n  - gold: ").Append(item.Gold.Replace("\n", " ")).Append("\n  - got: ").Append(answer).Append("\n");
            }

            return md.ToString();
        }

        /// <summary>
        /// Render an agent report.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown.</returns>
        public static string RenderAgent(AgentReport report)
        {
            StringBuilder md = new StringBuilder();
            md.Append("# Agent benchmark: ").Append(report.Suite).Append("\n\n");
            AppendEnvironment(md, report.Environment, report.Config);
            md.Append("## By arm\n\n| Arm | Tasks | Success | Errors | Mean turns | Mean cost (USD) | Total cost (USD) | Mean duration (s) |\n|---|---|---|---|---|---|---|---|\n");
            foreach (KeyValuePair<string, Dictionary<string, double>> arm in report.Arms)
            {
                md.Append("| ").Append(arm.Key).Append(" | ").Append(arm.Value["tasks"]).Append(" | ").Append(arm.Value["successRate"].ToString("P0")).Append(" | ").Append(arm.Value["errors"])
                  .Append(" | ").Append(arm.Value["meanTurns"]).Append(" | ").Append(arm.Value["meanCostUsd"].ToString("F4")).Append(" | ").Append(arm.Value["totalCostUsd"].ToString("F4"))
                  .Append(" | ").Append((arm.Value["meanDurationMs"] / 1000.0).ToString("F1")).Append(" |\n");
            }

            md.Append("\n## Tasks\n\n| Task | Type | ").Append(string.Join(" | ", report.Arms.Keys)).Append(" |\n|---|---|").Append(string.Concat(Enumerable.Repeat("---|", report.Arms.Count))).Append("\n");
            foreach (IGrouping<string, AgentItem> task in report.Items.GroupBy(i => i.TaskId))
            {
                md.Append("| ").Append(task.Key).Append(" | ").Append(task.First().Type).Append(" | ");
                md.Append(string.Join(" | ", report.Arms.Keys.Select(a =>
                {
                    AgentItem? item = task.FirstOrDefault(i => i.Arm == a);
                    return item == null ? "-" : (item.Success ? "pass" : (item.Error != null ? "error" : "fail")) + " (" + item.Turns + "t)";
                })));
                md.Append(" |\n");
            }

            md.Append("\n## Failed isis-arm answers\n\n");
            foreach (AgentItem item in report.Items.Where(i => i.Arm == "isis" && !i.Success))
            {
                string answer = item.Answer.Replace("\n", " ");
                if (answer.Length > 300) answer = answer.Substring(0, 300) + "…";
                md.Append("- `").Append(item.TaskId).Append("`: ").Append(item.Error ?? answer).Append("\n");
            }

            return md.ToString();
        }

        /// <summary>
        /// Append the environment and configuration block.
        /// </summary>
        /// <param name="md">Builder.</param>
        /// <param name="environment">Environment.</param>
        /// <param name="config">Configuration.</param>
        public static void AppendEnvironment(StringBuilder md, BenchmarkEnvironment environment, Dictionary<string, string> config)
        {
            md.Append("| | |\n|---|---|\n");
            md.Append("| Started (UTC) | ").Append(environment.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(" |\n");
            md.Append("| Server | ").Append(environment.ServerUrl).Append(" |\n");
            md.Append("| Commit | ").Append(environment.GitCommit).Append(" |\n");
            md.Append("| Machine | ").Append(environment.Machine).Append(" |\n");
            md.Append("| Embedding | ").Append(environment.Embedding).Append(" |\n");
            if (!string.IsNullOrEmpty(environment.Inference)) md.Append("| Inference | ").Append(environment.Inference).Append(" |\n");
            foreach (KeyValuePair<string, string> item in config) md.Append("| ").Append(item.Key).Append(" | ").Append(item.Value).Append(" |\n");
            md.Append("\n");
        }

        /// <summary>
        /// Append a stage breakdown table.
        /// </summary>
        /// <param name="md">Builder.</param>
        /// <param name="stages">Stages.</param>
        public static void AppendStages(StringBuilder md, Dictionary<string, StageBreakdown> stages)
        {
            if (stages == null || stages.Count == 0) return;
            md.Append("| Server stage | Calls | Mean ms | Total s |\n|---|---|---|---|\n");
            foreach (KeyValuePair<string, StageBreakdown> stage in stages.OrderBy(s => StageOrder(s.Key)))
            {
                md.Append("| ").Append(stage.Key).Append(" | ").Append(stage.Value.Count).Append(" | ").Append(stage.Value.MeanMs).Append(" | ").Append(Math.Round(stage.Value.TotalMs / 1000.0, 2)).Append(" |\n");
            }

            md.Append("\n");
        }

        /// <summary>
        /// Format a metric value.
        /// </summary>
        /// <param name="metrics">Metrics.</param>
        /// <param name="name">Name.</param>
        /// <returns>Three-decimal value or n/a.</returns>
        public static string Value(Dictionary<string, double> metrics, string name)
        {
            return metrics != null && metrics.TryGetValue(name, out double value) ? value.ToString("F3") : "n/a";
        }

        #endregion

        #region Private-Methods

        private static int StageOrder(string stage)
        {
            string[] order = PrometheusSnapshot.StageFamilies.Select(f => f.Replace("isis_", string.Empty).Replace("_duration_seconds", string.Empty)).ToArray();
            int index = Array.IndexOf(order, stage);
            return index < 0 ? int.MaxValue : index;
        }

        private static void AppendMisses(StringBuilder md, RetrievalReport report)
        {
            // Queries that every mode missed in the top 5 are the most informative for tuning.
            List<IGrouping<string, QueryOutcome>> byQuery = report.Outcomes
                .Where(o => o.Metrics.Count > 0)
                .GroupBy(o => o.Corpus + "/" + o.QueryId)
                .Where(g => g.All(o => o.Metrics["recall@5"] == 0.0))
                .ToList();

            md.Append("\n## Queries every mode missed in the top 5 (").Append(byQuery.Count).Append(")\n\n");
            foreach (IGrouping<string, QueryOutcome> group in byQuery.Take(40))
            {
                QueryOutcome first = group.First();
                md.Append("- `").Append(group.Key).Append("` (").Append(first.Type).Append(") wanted ").Append(string.Join(", ", first.Relevant))
                  .Append("; got ").Append(string.Join(", ", first.Ranked.Take(3))).Append("\n");
            }
        }

        #endregion
    }
}
