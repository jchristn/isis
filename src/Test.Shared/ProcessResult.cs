namespace Test.Shared
{
    /// <summary>
    /// The result of running an external process.
    /// </summary>
    internal sealed class ProcessResult
    {
        #region Internal-Members

        /// <summary>
        /// The process exit code.
        /// </summary>
        internal int ExitCode { get; set; } = -1;

        /// <summary>
        /// Captured standard output.
        /// </summary>
        internal string Output { get; set; } = string.Empty;

        /// <summary>
        /// Captured standard error.
        /// </summary>
        internal string Error { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        internal ProcessResult()
        {
        }

        #endregion
    }
}
