namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Automated Isis test runner.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            return await ConsoleRunner.RunAsync(
                IsisSuites.GetSuites(),
                resultsPath: ParseResultsPath(args)).ConfigureAwait(false);
        }

        private static string? ParseResultsPath(string[] args)
        {
            if (args == null || args.Length < 2) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (String.Equals(args[i], "--results", StringComparison.Ordinal)) return args[i + 1];
            }

            return null;
        }
    }
}
