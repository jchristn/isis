namespace Isis.Server.Models
{
    /// <summary>
    /// A request to discover which tenants an email address belongs to, used by the pre-auth login step.
    /// </summary>
    public class TenantsForEmailRequest
    {
        #region Public-Members

        /// <summary>
        /// The email address to look up.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a tenants-for-email request.
        /// </summary>
        public TenantsForEmailRequest()
        {
        }

        #endregion
    }
}
