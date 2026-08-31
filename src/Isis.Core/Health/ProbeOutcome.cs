namespace Isis.Core.Health
{
    /// <summary>
    /// The outcome of a single endpoint health probe.
    /// </summary>
    internal sealed class ProbeOutcome
    {
        #region Internal-Members

        /// <summary>
        /// Whether the probe was considered successful.
        /// </summary>
        internal bool Success { get; set; } = false;

        /// <summary>
        /// The HTTP status code returned, if any.
        /// </summary>
        internal int StatusCode { get; set; } = 0;

        /// <summary>
        /// The error message, when the probe failed.
        /// </summary>
        internal string? Error { get; set; } = null;

        /// <summary>
        /// The round-trip latency of the probe, in milliseconds.
        /// </summary>
        internal int LatencyMs { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        internal ProbeOutcome()
        {
        }

        #endregion
    }
}
