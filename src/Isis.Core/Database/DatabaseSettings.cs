namespace Isis.Core.Database
{
    using System;
    using Isis.Core.Enums;

    /// <summary>
    /// Connection settings for the Isis relational metadata store.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// The database provider type. Default is <see cref="DatabaseTypeEnum.Sqlite"/>.
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// The SQLite database filename. Used only when <see cref="Type"/> is Sqlite.
        /// </summary>
        public string Filename { get; set; } = "data/isis.db";

        /// <summary>
        /// The database server hostname. Used for server-based providers.
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// The database server port. When zero, the provider default is used. Range 0 to 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                if (value < 0 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 0 and 65535.");
                _Port = value;
            }
        }

        /// <summary>
        /// The database name for server-based providers.
        /// </summary>
        public string DatabaseName { get; set; } = "isis";

        /// <summary>
        /// The database username for server-based providers.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// The database password for server-based providers.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// The named instance for SQL Server, if any.
        /// </summary>
        public string? Instance { get; set; } = null;

        /// <summary>
        /// The schema / search path for PostgreSQL, if any.
        /// </summary>
        public string? Schema { get; set; } = null;

        /// <summary>
        /// When true, executed queries are logged via the driver's query-log action.
        /// </summary>
        public bool LogQueries { get; set; } = false;

        /// <summary>
        /// When true, encrypted connections are required for server-based providers.
        /// </summary>
        public bool RequireEncryption { get; set; } = false;

        #endregion

        #region Private-Members

        private int _Port = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate database settings.
        /// </summary>
        public DatabaseSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the default port for the configured provider.
        /// </summary>
        /// <returns>The default port, or zero for file-based providers.</returns>
        public int GetDefaultPort()
        {
            switch (Type)
            {
                case DatabaseTypeEnum.Mysql:
                    return 3306;
                case DatabaseTypeEnum.Postgresql:
                    return 5432;
                case DatabaseTypeEnum.SqlServer:
                    return 1433;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Get the effective port, preferring the explicit port and falling back to the provider default.
        /// </summary>
        /// <returns>The effective port.</returns>
        public int GetEffectivePort()
        {
            return _Port > 0 ? _Port : GetDefaultPort();
        }

        #endregion
    }
}
