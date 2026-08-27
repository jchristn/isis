namespace Isis.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Isis.Core.Helpers;

    /// <summary>
    /// An interactive principal scoped to a tenant.
    /// </summary>
    public class User
    {
        #region Public-Members

        /// <summary>
        /// User identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Owning tenant identifier. May not be set to null or empty.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// First name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        /// <summary>
        /// Email address. Unique within a tenant. May not be set to null or empty.
        /// </summary>
        public string Email
        {
            get
            {
                return _Email;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Email));
                _Email = value;
            }
        }

        /// <summary>
        /// SHA-256 hash of the user's password. Never serialized to API responses.
        /// </summary>
        [JsonIgnore]
        public string? PasswordSha256 { get; set; } = null;

        /// <summary>
        /// When true, the user has system-wide administrative privileges, bypassing tenant and permission checks.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// When true, the user has full administrative access within its own tenant, bypassing RBAC checks for that tenant.
        /// </summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>
        /// Indicates whether the user is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Indicates whether the user is protected from deletion.
        /// </summary>
        public bool Protected { get; set; } = false;

        /// <summary>
        /// UTC timestamp when the user was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the user was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.User();
        private string _TenantId = String.Empty;
        private string _Email = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a user.
        /// </summary>
        public User()
        {
        }

        #endregion
    }
}
