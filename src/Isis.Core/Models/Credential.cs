namespace Isis.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// A non-interactive principal (automation/integration) bound to exactly one tenant and owning user.
    /// </summary>
    public class Credential
    {
        #region Public-Members

        /// <summary>
        /// Credential identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// Owning user identifier. May not be set to null or empty.
        /// </summary>
        public string UserId
        {
            get
            {
                return _UserId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(UserId));
                _UserId = value;
            }
        }

        /// <summary>
        /// Human-readable credential name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Public access key used for lookup.
        /// </summary>
        public string AccessKey
        {
            get
            {
                return _AccessKey;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(AccessKey));
                _AccessKey = value;
            }
        }

        /// <summary>
        /// Private secret key material used for comparison or signature validation. Never serialized to API
        /// responses; the raw secret is returned to the caller only once, at creation time.
        /// </summary>
        [JsonIgnore]
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// The credential's authentication mode.
        /// </summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.DirectHeader;

        /// <summary>
        /// Indicates whether the credential is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Indicates whether the credential is protected from deletion.
        /// </summary>
        public bool Protected { get; set; } = false;

        /// <summary>
        /// UTC timestamp when the credential was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the credential was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the credential was last used, if ever.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the credential expires, if ever.
        /// </summary>
        public DateTime? ExpirationUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Credential();
        private string _TenantId = String.Empty;
        private string _UserId = String.Empty;
        private string _AccessKey = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a credential.
        /// </summary>
        public Credential()
        {
        }

        #endregion
    }
}
