namespace Isis.Core.Security
{
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// The authenticated context for a request, attached to the Watson HttpContext metadata and read by
    /// route handlers for authorization.
    /// </summary>
    public class RequestContext
    {
        #region Public-Members

        /// <summary>
        /// Whether the request is authenticated.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// The type of principal, when authenticated.
        /// </summary>
        public PrincipalTypeEnum? PrincipalType { get; set; } = null;

        /// <summary>
        /// The tenant identifier, when resolved. Null for a system administrator not bound to a tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// The user identifier, when the principal is a user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// The credential identifier, when the principal is a credential.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// The backing session identifier, when authenticated by a session token.
        /// </summary>
        public string? SessionId { get; set; } = null;

        /// <summary>
        /// Whether the principal has system-wide administrative access (bypasses all checks).
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the principal has tenant-wide administrative access within its tenant.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// The resolved display name of the principal, when available.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// The resolved user, when the principal is a user.
        /// </summary>
        public User? User { get; set; } = null;

        /// <summary>
        /// The resolved credential, when the principal is a credential.
        /// </summary>
        public Credential? Credential { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build an unauthenticated context.
        /// </summary>
        /// <returns>An unauthenticated request context.</returns>
        public static RequestContext Unauthenticated()
        {
            return new RequestContext();
        }

        #endregion
    }
}
