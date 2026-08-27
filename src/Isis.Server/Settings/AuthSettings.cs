namespace Isis.Server.Settings
{
    /// <summary>
    /// Authentication settings.
    /// </summary>
    public class AuthSettings
    {
        #region Public-Members

        /// <summary>
        /// The token issuer name.
        /// </summary>
        public string Issuer { get; set; } = "isis";

        /// <summary>
        /// The lifetime, in minutes, of a session token issued by email/password login.
        /// </summary>
        public int SessionLifetimeMinutes { get; set; } = 1440;

        /// <summary>
        /// The email address of the default administrator user seeded on first boot. This user has IsAdmin set
        /// and is the bootstrap login for a fresh deployment.
        /// </summary>
        public string SeedAdminEmail { get; set; } = "admin@isis.local";

        /// <summary>
        /// The password seeded on the default administrator user. Override via environment for anything beyond
        /// local development.
        /// </summary>
        public string SeedAdminPassword { get; set; } = "isisadmin";

        /// <summary>
        /// The access key seeded on the default tenant credential, presented in the x-access-key header.
        /// Override via environment for anything beyond local development.
        /// </summary>
        public string DefaultAccessKey { get; set; } = "isisdefaultkey";

        /// <summary>
        /// The secret key seeded on the default tenant credential, presented in the x-secret-key header.
        /// Override via environment for anything beyond local development.
        /// </summary>
        public string DefaultSecretKey { get; set; } = "isisdefaultsecret";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate authentication settings.
        /// </summary>
        public AuthSettings()
        {
        }

        #endregion
    }
}
