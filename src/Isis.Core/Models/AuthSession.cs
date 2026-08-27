namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// A revocable, tenant-bound authentication session referring to a single principal.
    /// </summary>
    public class AuthSession
    {
        #region Public-Members

        /// <summary>
        /// Session identifier. Defaults to a generated value; may not be set to null or empty.
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
        /// User identifier, when the principal is a user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential identifier, when the principal is a credential.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// The type of principal this session represents.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// The authentication scheme used to establish the session.
        /// </summary>
        public AuthSchemeEnum AuthScheme { get; set; } = AuthSchemeEnum.BearerToken;

        /// <summary>
        /// The opaque bearer token or nonce identifying the session.
        /// </summary>
        public string Token
        {
            get
            {
                return _Token;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Token));
                _Token = value;
            }
        }

        /// <summary>
        /// Source IP address of the request that created the session.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent of the request that created the session.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the session was issued.
        /// </summary>
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the session expires.
        /// </summary>
        public DateTime ExpirationUtc { get; set; } = DateTime.UtcNow.AddHours(24);

        /// <summary>
        /// UTC timestamp when the session was last used, if ever.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the session was revoked, if ever.
        /// </summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>
        /// Reason the session was revoked, if applicable.
        /// </summary>
        public string? RevocationReason { get; set; } = null;

        /// <summary>
        /// Indicates whether the session is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// UTC timestamp when the session record was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the session record was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Session();
        private string _TenantId = String.Empty;
        private string _Token = IdGenerator.Token();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an authentication session.
        /// </summary>
        public AuthSession()
        {
        }

        #endregion
    }
}
