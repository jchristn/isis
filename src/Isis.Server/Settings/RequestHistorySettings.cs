namespace Isis.Server.Settings
{
    /// <summary>
    /// Request history capture settings.
    /// </summary>
    public class RequestHistorySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether request history capture is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether to capture request and response headers (sensitive headers are redacted).
        /// </summary>
        public bool CaptureHeaders { get; set; } = true;

        /// <summary>
        /// Whether to capture request and response bodies (truncated to <see cref="MaxBodyBytes"/>; known
        /// secret fields are redacted).
        /// </summary>
        public bool CaptureBodies { get; set; } = true;

        /// <summary>
        /// Maximum number of bytes of a request or response body to retain; longer bodies are truncated.
        /// </summary>
        public int MaxBodyBytes { get; set; } = 16384;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate request history settings.
        /// </summary>
        public RequestHistorySettings()
        {
        }

        #endregion
    }
}
