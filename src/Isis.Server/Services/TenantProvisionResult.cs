namespace Isis.Server.Services
{
    using Isis.Core.Models;

    /// <summary>
    /// The result of provisioning a tenant: the created tenant plus the auto-generated admin credentials and
    /// default credential. The password and secret key are plaintext and must be shown to the operator once.
    /// </summary>
    public class TenantProvisionResult
    {
        #region Public-Members

        /// <summary>
        /// The created tenant.
        /// </summary>
        public Tenant Tenant { get; set; } = null!;

        /// <summary>
        /// The generated tenant-admin user identifier.
        /// </summary>
        public string AdminUserId { get; set; } = string.Empty;

        /// <summary>
        /// The generated tenant-admin email address.
        /// </summary>
        public string AdminEmail { get; set; } = string.Empty;

        /// <summary>
        /// The generated tenant-admin plaintext password (shown once).
        /// </summary>
        public string AdminPassword { get; set; } = string.Empty;

        /// <summary>
        /// The generated default credential identifier.
        /// </summary>
        public string CredentialId { get; set; } = string.Empty;

        /// <summary>
        /// The generated credential access key.
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// The generated credential raw secret key (shown once).
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;

        #endregion
    }
}
