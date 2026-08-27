namespace Isis.Server.Models
{
    /// <summary>
    /// An email/password login request body used to issue a session token.
    /// </summary>
    public class LoginRequest
    {
        #region Public-Members

        /// <summary>
        /// The user's email address.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The user's plaintext password.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// The identifier of the tenant the user is logging into.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a login request.
        /// </summary>
        public LoginRequest()
        {
        }

        #endregion
    }
}
