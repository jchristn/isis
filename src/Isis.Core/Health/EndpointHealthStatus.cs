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

        /// <summary>
        /// The UTC timestamp of the very first probe, if any (when monitoring of this endpoint began).
        /// </summary>
        public DateTime? FirstCheckUtc { get; set; } = null;

        /// <summary>
        /// The UTC timestamp of the last successful probe, if any.
        /// </summary>
        public DateTime? LastHealthyUtc { get; set; } = null;

        /// <summary>
        /// The UTC timestamp of the last failed probe, if any.
        /// </summary>
        public DateTime? LastUnhealthyUtc { get; set; } = null;

        /// <summary>
        /// The UTC timestamp of the last change to the healthy/unhealthy state, if any.
        /// </summary>
        public DateTime? LastStateChangeUtc { get; set; } = null;

        /// <summary>
        /// The round-trip latency of the last probe, in milliseconds (0 when never probed).
        /// </summary>
        public int LastLatencyMs { get; set; } = 0;

        /// <summary>
        /// Total accumulated time (ms) the endpoint has been observed healthy, sampled across probes.
        /// </summary>
        public long TotalUptimeMs { get; set; } = 0;

        /// <summary>
        /// Total accumulated time (ms) the endpoint has been observed unhealthy, sampled across probes.
        /// </summary>
        public long TotalDowntimeMs { get; set; } = 0;

        /// <summary>
        /// The observed uptime as a percentage of total sampled time (0 when nothing sampled yet).
        /// </summary>
        public double UptimePercentage
        {
            get
            {
                long total = TotalUptimeMs + TotalDowntimeMs;
                if (total <= 0) return IsHealthy ? 100.0 : 0.0;
                return (double)TotalUptimeMs / total * 100.0;
            }
        }

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
