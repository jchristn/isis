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
