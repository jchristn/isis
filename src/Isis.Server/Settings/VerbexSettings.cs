namespace Isis.Server.Settings
{
    /// <summary>
    /// Verbex integration settings.
    /// </summary>
    public class VerbexSettings
    {
        #region Public-Members

        /// <summary>
        /// The Verbex server endpoint. Null disables the Verbex store.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate Verbex settings.
        /// </summary>
        public VerbexSettings()
        {
        }

        #endregion
    }
}
