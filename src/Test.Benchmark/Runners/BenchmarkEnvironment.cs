namespace Test.Benchmark.Runners
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Where and against what a benchmark ran, recorded in every report so results are comparable over time.
    /// </summary>
    public class BenchmarkEnvironment
    {
        #region Public-Members

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Isis REST base URL.
        /// </summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>
        /// Git commit of the working tree (with "-dirty" when there are uncommitted changes).
        /// </summary>
        public string GitCommit { get; set; } = string.Empty;

        /// <summary>
        /// Machine description.
        /// </summary>
        public string Machine { get; set; } = string.Empty;

        /// <summary>
        /// Embedding endpoint description (format, model, URL).
        /// </summary>
        public string Embedding { get; set; } = string.Empty;

        /// <summary>
        /// Inference endpoint description, when used.
        /// </summary>
        public string? Inference { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Capture the current environment.
        /// </summary>
        /// <param name="serverUrl">Isis REST base URL.</param>
        /// <param name="embedding">Embedding endpoint description.</param>
        /// <returns>The environment.</returns>
        public static BenchmarkEnvironment Capture(string serverUrl, string embedding)
        {
            return new BenchmarkEnvironment
            {
                ServerUrl = serverUrl,
                Embedding = embedding,
                GitCommit = GitCommitOrUnknown(),
                Machine = Environment.MachineName + " / " + RuntimeInformation.OSDescription + " / " + Environment.ProcessorCount + " logical CPUs / .NET " + Environment.Version
            };
        }

        #endregion

        #region Private-Methods

        private static string GitCommitOrUnknown()
        {
            try
            {
                string commit = RunGit("rev-parse --short HEAD").Trim();
                string status = RunGit("status --porcelain --untracked-files=no").Trim();
                return string.IsNullOrEmpty(commit) ? "unknown" : commit + (status.Length > 0 ? "-dirty" : string.Empty);
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string RunGit(string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? process = Process.Start(info);
            if (process == null) return string.Empty;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }

        #endregion
    }
}
