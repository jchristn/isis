namespace Isis.Server.Settings
{
    /// <summary>
    /// Retention settings for the observability history tables (request history and operation events).
    /// A background pruner periodically deletes rows older than <see cref="MaxAgeDays"/> from both tables.
    /// </summary>
    public class RetentionSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the background retention pruner is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum age, in days, to retain request-history entries and operation events. Rows older than this
        /// are deleted on each sweep. Values below 1 are treated as 1.
        /// </summary>
        public int MaxAgeDays { get; set; } = 30;

        /// <summary>
        /// Interval, in minutes, between retention sweeps. Values below 1 are treated as 1.
        /// </summary>
        public int SweepIntervalMinutes { get; set; } = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate retention settings.
        /// </summary>
        public RetentionSettings()
        {
        }

        #endregion
    }
}
