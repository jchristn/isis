namespace Isis.Core.Health
{
    using System;

    /// <summary>
    /// The current in-memory health state of a model endpoint.
    /// </summary>
    public class EndpointHealthStatus
    {
        #region Public-Members

        /// <summary>
        /// The endpoint identifier.
        /// </summary>
        public string EndpointId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the endpoint is currently considered healthy.
        /// </summary>
        public bool IsHealthy { get; set; } = false;

        /// <summary>
        /// Whether any probe has been performed yet.
        /// </summary>
        public bool Probed { get; set; } = false;

        /// <summary>
        /// The UTC timestamp of the last probe, if any.
        /// </summary>
        public DateTime? LastCheckUtc { get; set; } = null;

        /// <summary>
        /// The HTTP status code returned by the last probe.
        /// </summary>
        public int LastStatusCode { get; set; } = 0;

        /// <summary>
        /// The number of consecutive successful probes.
        /// </summary>
        public int ConsecutiveSuccesses { get; set; } = 0;

        /// <summary>
        /// The number of consecutive failed probes.
        /// </summary>
        public int ConsecutiveFailures { get; set; } = 0;

        /// <summary>
        /// The error from the last failed probe, if any.
        /// </summary>
        public string? LastError { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an endpoint health status.
        /// </summary>
        public EndpointHealthStatus()
        {
        }

        #endregion
    }
}
