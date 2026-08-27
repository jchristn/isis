namespace Isis.Server.Models
{
    /// <summary>
    /// A create/update request body for a user. On create, Password is required; on update, a null or empty
    /// Password leaves the existing password unchanged.
    /// </summary>
    public class UserUpsertRequest
    {
        #region Public-Members

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Email address. Required on create.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Plaintext password. Required on create; optional on update.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Whether the user has system-wide administrative privileges.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user has tenant-wide administrative privileges.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Whether the user is active.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a user upsert request.
        /// </summary>
        public UserUpsertRequest()
        {
        }

        #endregion
    }
}
