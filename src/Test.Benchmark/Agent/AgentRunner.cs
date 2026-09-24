namespace Test.Benchmark.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Agent-in-the-loop benchmark: runs Claude Code headless (claude -p) on memory-dependent tasks in two arms,
    /// with the Isis MCP server connected ("isis") and without any memory ("none"), and grades the final answers.
    /// Each run happens in a fresh empty directory with every built-in tool disabled, so the agent can only know the
    /// answer through Isis. It cannot read the repository or its own auto-memory.
    /// </summary>
    public class AgentRunner
    {
        #region Private-Members

        private readonly BenchmarkContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Benchmark context.</param>
        public AgentRunner(BenchmarkContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the benchmark.
        /// </summary>
        /// <param name="taskPath">Task file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The report.</returns>
        public async Task<AgentReport> RunAsync(string taskPath, CancellationToken token)
        {
            BenchmarkArguments args = _Context.Arguments;
            AgentTaskFile? suite = JsonSerializer.Deserialize<AgentTaskFile>(File.ReadAllText(taskPath), DatasetStore.Json);
            if (suite == null || suite.Tasks.Count == 0) throw new InvalidDataException("'" + taskPath + "' has no tasks.");

            string datasetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(taskPath)) ?? ".", suite.Dataset));
            BenchmarkDataset dataset = DatasetStore.Load(datasetPath);
            dataset.Corpora = dataset.Corpora.Take(1).ToList();

            string model = args.Get("model", "haiku");
            string mcpUrl = args.Get("mcp-url", "http://127.0.0.1:18720/mcp");
            List<string> arms = args.GetList("arms", "isis,none");
            int limit = args.GetInt("limit", 0);
            double budget = args.GetDouble("max-budget-usd", 0.50);
            string claude = args.Get("claude-path", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "claude.cmd" : "claude");

            AgentReport report = new AgentReport { Suite = suite.Name, Environment = _Context.Environment };
            report.Config["model"] = model;
            report.Config["mcpUrl"] = mcpUrl;
            report.Config["arms"] = string.Join(",", arms);
            report.Config["dataset"] = dataset.Name;
            report.Config["maxBudgetUsdPerRun"] = budget.ToString("F2");

            IngestSummary ingest = new IngestSummary();
            List<ProvisionedScope> scopes = await new ScopeProvisioner(_Context).ProvisionAsync(dataset, ingest, token).ConfigureAwait(false);
            string scopeId = scopes[0].ScopeId;
            Console.WriteLine("[agent] memory scope " + scopeId + " (" + dataset.Corpora[0].Documents.Count + " memories)");

            List<AgentTask> tasks = limit > 0 ? suite.Tasks.Take(limit).ToList() : suite.Tasks;
            string root = Path.Combine(Path.GetTempPath(), "isis-agent-bench-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            try
            {
                string mcpConfig = Path.Combine(root, "mcp.json");
                JsonObject servers = new JsonObject
                {
                    ["mcpServers"] = new JsonObject
                    {
                        ["isis"] = new JsonObject
                        {
                            ["type"] = "http",
                            ["url"] = mcpUrl,
                            ["headers"] = new JsonObject { ["x-access-key"] = args.Get("access-key", "isisdefaultkey") }
                        }
                    }
                };
                File.WriteAllText(mcpConfig, servers.ToJsonString());
                string emptyConfig = Path.Combine(root, "mcp-empty.json");
                File.WriteAllText(emptyConfig, "{\"mcpServers\":{}}");

                string isisPrompt = Path.Combine(root, "system-isis.txt");
                File.WriteAllText(isisPrompt,
                    "You are helping a developer on a software project. The project's durable memory lives in the Isis MCP server (tools named mcp__isis__*). "
                    + "Its memories are in tenantId '" + _Context.Client.TenantId + "', scopeId '" + scopeId + "'. "
                    + "Before answering a question about the project, search that memory (memory_search, then memory_read for detail) and answer from what you find. "
                    + "Answer concisely. If memory does not contain the answer, say so.");
                string nonePrompt = Path.Combine(root, "system-none.txt");
                File.WriteAllText(nonePrompt, "You are helping a developer on a software project. Answer concisely. If you do not know a project-specific fact, say so rather than guessing.");

                int index = 0;
                foreach (AgentTask task in tasks)
                {
                    index++;
                    foreach (string arm in arms)
                    {
                        string workdir = Path.Combine(root, task.Id + "-" + arm);
                        Directory.CreateDirectory(workdir);
                        bool withIsis = string.Equals(arm, "isis", StringComparison.OrdinalIgnoreCase);
                        AgentItem item = await RunOneAsync(claude, model, budget, task, arm, workdir, withIsis ? mcpConfig : emptyConfig, withIsis ? isisPrompt : nonePrompt, withIsis, token).ConfigureAwait(false);
                        report.Items.Add(item);
                        Console.WriteLine("  [" + index + "/" + tasks.Count + "] " + task.Id + " " + arm.PadRight(5) + (item.Success ? " PASS" : " fail") + "  turns " + item.Turns + "  $" + item.CostUsd.ToString("F4")
                            + (item.Error != null ? "  error: " + item.Error : string.Empty));
                    }
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }

            foreach (IGrouping<string, AgentItem> group in report.Items.GroupBy(i => i.Arm))
            {
                List<AgentItem> items = group.ToList();
                report.Arms[group.Key] = new Dictionary<string, double>
                {
                    ["tasks"] = items.Count,
                    ["successRate"] = Math.Round(items.Average(i => i.Success ? 1.0 : 0.0), 4),
                    ["errors"] = items.Count(i => i.Error != null),
                    ["meanTurns"] = Math.Round(items.Average(i => i.Turns), 2),
                    ["meanCostUsd"] = Math.Round(items.Average(i => i.CostUsd), 5),
                    ["totalCostUsd"] = Math.Round(items.Sum(i => i.CostUsd), 4),
                    ["meanDurationMs"] = Math.Round(items.Average(i => i.DurationMs), 0)
                };
            }

            foreach (KeyValuePair<string, Dictionary<string, double>> arm in report.Arms)
            {
                Console.WriteLine("[agent] " + arm.Key.PadRight(5) + " success " + arm.Value["successRate"].ToString("P0") + "  mean turns " + arm.Value["meanTurns"] + "  total $" + arm.Value["totalCostUsd"]);
            }

            return report;
        }

        #endregion

        #region Private-Methods

        private static async Task<AgentItem> RunOneAsync(string claude, string model, double budget, AgentTask task, string arm, string workdir, string mcpConfig, string systemPromptFile, bool withIsis, CancellationToken token)
        {
            AgentItem item = new AgentItem { TaskId = task.Id, Type = task.Type, Arm = arm };
            ProcessStartInfo info = new ProcessStartInfo(claude)
            {
                WorkingDirectory = workdir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            // The prompt goes over stdin and the system prompt through a file, so no free text is ever parsed by a
            // shell (claude is a .cmd shim on Windows).
            info.ArgumentList.Add("-p");
            info.ArgumentList.Add("--output-format");
            info.ArgumentList.Add("json");
            info.ArgumentList.Add("--model");
            info.ArgumentList.Add(model);
            info.ArgumentList.Add("--no-session-persistence");
            info.ArgumentList.Add("--strict-mcp-config");
            info.ArgumentList.Add("--mcp-config");
            info.ArgumentList.Add(mcpConfig);
            info.ArgumentList.Add("--tools");
            info.ArgumentList.Add(string.Empty);
            if (withIsis)
            {
                info.ArgumentList.Add("--allowedTools");
                info.ArgumentList.Add("mcp__isis");
            }

            info.ArgumentList.Add("--append-system-prompt-file");
            info.ArgumentList.Add(systemPromptFile);
            info.ArgumentList.Add("--max-budget-usd");
            info.ArgumentList.Add(budget.ToString(CultureInfo.InvariantCulture));

            Stopwatch wall = Stopwatch.StartNew();
            using Process? process = Process.Start(info);
            if (process == null)
            {
                item.Error = "could not start " + claude;
                return item;
            }

            await process.StandardInput.WriteAsync(task.Prompt).ConfigureAwait(false);
            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(token);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                process.Kill(true);
                item.Error = "timed out";
                return item;
            }

            string output = await stdout.ConfigureAwait(false);
            string errors = await stderr.ConfigureAwait(false);
            item.DurationMs = Math.Round(wall.Elapsed.TotalMilliseconds, 0);

            JsonNode? result = null;
            try
            {
                int start = output.IndexOf('{');
                result = start >= 0 ? JsonNode.Parse(output.Substring(start)) : null;
            }
            catch (JsonException)
            {
            }

            if (result == null)
            {
                item.Error = "unparseable output (exit " + process.ExitCode + "): " + Truncate(output + " " + errors, 300);
                return item;
            }

            string answer = result["result"]?.GetValue<string>() ?? string.Empty;
            item.Answer = Truncate(answer, 2000);
            item.Turns = result["num_turns"]?.GetValue<int>() ?? 0;
            item.CostUsd = result["total_cost_usd"]?.GetValue<double>() ?? 0.0;
            if (result["is_error"]?.GetValue<bool>() == true) item.Error = Truncate(result["subtype"]?.ToString() + " " + answer, 300);

            bool expected = task.Expect.All(p => Regex.IsMatch(answer, p, RegexOptions.IgnoreCase | RegexOptions.Singleline));
            bool forbidden = task.Forbid.Any(p => Regex.IsMatch(answer, p, RegexOptions.IgnoreCase | RegexOptions.Singleline));
            item.Success = item.Error == null && expected && !forbidden;
            return item;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length > max ? text.Substring(0, max) + "…" : text;
        }

        #endregion
    }
}
