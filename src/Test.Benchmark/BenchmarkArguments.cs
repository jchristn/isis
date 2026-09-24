namespace Test.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Parsed command line: a command word followed by <c>--name value</c> options and bare <c>--flag</c> switches.
    /// </summary>
    public class BenchmarkArguments
    {
        #region Public-Members

        /// <summary>
        /// The command word (first positional argument), lower-cased. Empty when none was given.
        /// </summary>
        public string Command { get; private set; } = string.Empty;

        #endregion

        #region Private-Members

        private readonly Dictionary<string, string> _Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse command-line arguments.
        /// </summary>
        /// <param name="args">The raw arguments.</param>
        /// <returns>The parsed arguments.</returns>
        public static BenchmarkArguments Parse(string[] args)
        {
            BenchmarkArguments parsed = new BenchmarkArguments();
            if (args == null) return parsed;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = arg.Substring(2);
                    bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                    parsed._Options[name] = hasValue ? args[++i] : "true";
                }
                else if (string.IsNullOrEmpty(parsed.Command))
                {
                    parsed.Command = arg.ToLowerInvariant();
                }
            }

            return parsed;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a string option.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <param name="defaultValue">Value when the option is absent.</param>
        /// <returns>The option value.</returns>
        public string Get(string name, string defaultValue)
        {
            return _Options.TryGetValue(name, out string? value) ? value : defaultValue;
        }

        /// <summary>
        /// Read an optional string option.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <returns>The option value, or null when absent.</returns>
        public string? GetOptional(string name)
        {
            return _Options.TryGetValue(name, out string? value) ? value : null;
        }

        /// <summary>
        /// Read an integer option.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <param name="defaultValue">Value when the option is absent.</param>
        /// <returns>The option value.</returns>
        public int GetInt(string name, int defaultValue)
        {
            return _Options.TryGetValue(name, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Read a floating-point option.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <param name="defaultValue">Value when the option is absent.</param>
        /// <returns>The option value.</returns>
        public double GetDouble(string name, double defaultValue)
        {
            return _Options.TryGetValue(name, out string? value) ? double.Parse(value, CultureInfo.InvariantCulture) : defaultValue;
        }

        /// <summary>
        /// Read a boolean switch. A bare <c>--flag</c> is true; <c>--flag false</c> is false.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <returns>True when the switch is set.</returns>
        public bool GetFlag(string name)
        {
            return _Options.TryGetValue(name, out string? value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Read a comma-separated list option.
        /// </summary>
        /// <param name="name">Option name without the leading dashes.</param>
        /// <param name="defaultValue">Comma-separated value when the option is absent.</param>
        /// <returns>The trimmed, non-empty list items.</returns>
        public List<string> GetList(string name, string defaultValue)
        {
            List<string> items = new List<string>();
            foreach (string part in Get(name, defaultValue).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                items.Add(part);
            }

            return items;
        }

        #endregion
    }
}
