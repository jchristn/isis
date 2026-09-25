namespace Isis.Server.Settings
{
    using System;

    /// <summary>
    /// Settings for the in-process lookup cache that holds credentials, users, scopes, and model endpoints for a few
    /// seconds, saving several database reads on every request.
    /// </summary>
    public class CacheSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the lookup cache is enabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Seconds a cached lookup is served before it is reloaded. Default 10, minimum 1, maximum 3600. Writes made
        /// through this node invalidate the affected entries immediately; the time to live bounds how long another
        /// node's change (for example a revoked credential) can go unnoticed here.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [1, 3600].</exception>
        public int TtlSeconds
        {
            get
            {
                return _TtlSeconds;
            }
            set
            {
                if (value < 1 || value > 3600) throw new ArgumentOutOfRangeException(nameof(TtlSeconds), "TtlSeconds must be between 1 and 3600.");
                _TtlSeconds = value;
            }
        }

        #endregion

        #region Private-Members

        private int _TtlSeconds = 10;

        #endregion
    }
}
