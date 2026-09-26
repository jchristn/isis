namespace Test.Benchmark.Reporting
{
    /// <summary>
    /// One round in a history report: its display name and how to find its report files. The selector is either a
    /// run label (the suffix after the dataset name, as passed to --label) or a UTC timestamp prefix of the report file
    /// name (for example 20260924-17 for runs made that hour without a label).
    /// </summary>
    public class RoundSelector
    {
        #region Public-Members

        /// <summary>
        /// Display name, for example "R5".
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Label or timestamp prefix.
        /// </summary>
        public string Selector { get; set; } = string.Empty;

        #endregion
    }
}
