namespace Isis.Server.Settings
{
    /// <summary>
    /// REST listener settings.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// The hostname to bind. Defaults to 127.0.0.1.
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// The port to listen on. Defaults to 8700.
        /// </summary>
        public int Port { get; set; } = 8700;

        /// <summary>
        /// Whether TLS is enabled.
        /// </summary>
        public bool Ssl { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate REST settings.
        /// </summary>
        public RestSettings()
        {
        }

        #endregion
    }
}
