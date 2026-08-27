namespace Isis.Server.Settings
{
    /// <summary>
    /// RecallDB integration settings.
    /// </summary>
    public class RecallDbSettings
    {
        #region Public-Members

        /// <summary>
        /// The RecallDB server endpoint. Null disables the RecallDB store.
        /// </summary>
        public string? Endpoint { get; set; } = "http://127.0.0.1:8600";

        /// <summary>
        /// The RecallDB admin API key. Override via environment; keep server-side only.
        /// </summary>
        public string? AdminApiKey { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate RecallDB settings.
        /// </summary>
        public RecallDbSettings()
        {
        }

        #endregion
    }
}
