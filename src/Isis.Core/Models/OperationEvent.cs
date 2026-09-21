namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A captured record of a single semantic operation (create, read, update, delete, search, chat, ...)
    /// against a resource type (scope, memory, category, ...). Derived from the HTTP request the server
    /// handled, and retained for observability charting alongside <see cref="RequestHistoryEntry"/>.
    /// </summary>
    public class OperationEvent
    {
        #region Public-Members

        /// <summary>
        /// Operation event identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// The tenant the operation resolved to, when authenticated.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// The resource type acted upon (for example "scope", "memory", "category", "search").
        /// </summary>
        public string ResourceType { get; set; } = string.Empty;

        /// <summary>
        /// The operation performed (for example "create", "read", "update", "delete", "search", "chat").
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// The identifier of the specific resource acted upon, when the request path carried one.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// The scope the operation targeted, when the request path was scope-qualified.
        /// </summary>
        public string? ScopeId { get; set; } = null;

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
        /// The principal name that performed the operation, when authenticated.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// The source IP address of the request, when known.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// The total request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0.0;

        /// <summary>
        /// UTC timestamp when the operation was handled.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Operation();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an operation event.
        /// </summary>
        public OperationEvent()
        {
        }

        #endregion
    }
}
