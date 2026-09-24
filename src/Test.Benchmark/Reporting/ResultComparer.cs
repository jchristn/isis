namespace Test.Benchmark.Reporting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Test.Benchmark.Datasets;
    using Test.Benchmark.Runners;

    /// <summary>
    /// Compares two retrieval reports and fails (exit code 1) when accuracy regresses beyond a tolerance, so a
    /// benchmark can gate changes in CI.
    /// </summary>
    public static class ResultComparer
    {
        #region Public-Methods

        /// <summary>
        /// Run the compare command.
        /// </summary>
        /// <param name="arguments">Arguments (--baseline, --candidate, --tolerance, --latency-tolerance).</param>
        /// <returns>0 when there is no regression, 1 when there is.</returns>
        public static int Compare(BenchmarkArguments arguments)
        {
            string baselinePath = arguments.Get("baseline", string.Empty);
            string candidatePath = arguments.Get("candidate", string.Empty);
            if (!File.Exists(baselinePath) || !File.Exists(candidatePath)) throw new FileNotFoundException("compare needs existing --baseline and --candidate report files.");

            double tolerance = arguments.GetDouble("tolerance", 0.01);
            double latencyTolerance = arguments.GetDouble("latency-tolerance", 0.0);
            RetrievalReport baseline = Load(baselinePath);
            RetrievalReport candidate = Load(candidatePath);

            Console.WriteLine("baseline : " + baselinePath + " (" + baseline.Environment.GitCommit + ")");
            Console.WriteLine("candidate: " + candidatePath + " (" + candidate.Environment.GitCommit + ")");
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-9} {1,-10} {2,9} {3,9} {4,9}", "mode", "metric", "baseline", "candidate", "delta"));

            bool regressed = false;
            foreach (ModeSummary candidateMode in candidate.Modes)
            {
                ModeSummary? baselineMode = baseline.Modes.Find(m => string.Equals(m.Mode, candidateMode.Mode, StringComparison.OrdinalIgnoreCase));
                if (baselineMode == null) continue;

                foreach (string metric in RetrievalRunner.MetricNames)
                {
                    if (!baselineMode.Metrics.TryGetValue(metric, out double before) || !candidateMode.Metrics.TryGetValue(metric, out double after)) continue;
                    double delta = after - before;
                    bool bad = delta < -tolerance;
                    regressed |= bad;
                    Console.WriteLine(string.Format("{0,-9} {1,-10} {2,9:F3} {3,9:F3} {4,9:+0.000;-0.000;0.000}{5}", candidateMode.Mode, metric, before, after, delta, bad ? "  REGRESSION" : string.Empty));
                }

                double p95Before = baselineMode.Latency.P95;
                double p95After = candidateMode.Latency.P95;
                bool slow = latencyTolerance > 0 && p95Before > 0 && p95After > p95Before * (1.0 + latencyTolerance);
                regressed |= slow;
                Console.WriteLine(string.Format("{0,-9} {1,-10} {2,9:F1} {3,9:F1} {4,9:+0.0;-0.0;0.0}{5}", candidateMode.Mode, "p95 ms", p95Before, p95After, p95After - p95Before, slow ? "  REGRESSION" : string.Empty));
            }

            Console.WriteLine();
            Console.WriteLine(regressed ? "RESULT: regression beyond tolerance" : "RESULT: no regression");
            return regressed ? 1 : 0;
        }

        #endregion

        #region Private-Methods

        private static RetrievalReport Load(string path)
        {
            RetrievalReport? report = JsonSerializer.Deserialize<RetrievalReport>(File.ReadAllText(path), DatasetStore.Json);
            if (report == null || !string.Equals(report.Kind, "retrieval", StringComparison.Ordinal))
                throw new InvalidDataException("'" + path + "' is not a retrieval report.");
            return report;
        }

        #endregion
    }
}
