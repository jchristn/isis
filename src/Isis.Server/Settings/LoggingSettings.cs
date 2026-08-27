namespace Isis.Server.Settings
{
    /// <summary>
    /// Logging settings.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to log to the console.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether database queries are logged.
        /// </summary>
        public bool LogQueries { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate logging settings.
        /// </summary>
        public LoggingSettings()
        {
        }

        #endregion
    }
}
