namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A captured record of a single HTTP request handled by the server.
    /// </summary>
    public class RequestHistoryEntry
    {
        #region Public-Members

        /// <summary>
        /// Request history entry identifier. Defaults to a generated value; may not be set to null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// The tenant the request resolved to, when authenticated.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// The HTTP method.
        /// </summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// The request path (including query string).
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The response status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// The source IP address of the request, when known.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// The principal name that made the request, when authenticated.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// The request headers, serialized as a JSON object mapping header name to value, when captured.
        /// </summary>
        public string? RequestHeaders { get; set; } = null;

        /// <summary>
        /// The request body, when captured.
        /// </summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>
        /// The response headers, serialized as a JSON object mapping header name to value, when captured.
        /// </summary>
        public string? ResponseHeaders { get; set; } = null;

        /// <summary>
        /// The response body, when captured.
        /// </summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>
        /// The total request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0.0;

        /// <summary>
        /// UTC timestamp when the request was handled.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Request();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a request history entry.
        /// </summary>
        public RequestHistoryEntry()
        {
        }

        #endregion
    }
}
