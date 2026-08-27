namespace Isis.Core.Database
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Database.Mysql;
    using Isis.Core.Database.Postgresql;
    using Isis.Core.Database.Sqlite;
    using Isis.Core.Database.SqlServer;

    /// <summary>
    /// Factory for creating database drivers from settings.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a database driver for the configured provider.
        /// </summary>
        /// <param name="settings">The database settings.</param>
        /// <returns>A database driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="NotSupportedException">Thrown when the configured provider is not yet implemented.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings);
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings);
                case DatabaseTypeEnum.Mysql:
                    return new MysqlDatabaseDriver(settings);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlServerDatabaseDriver(settings);
                default:
                    throw new NotSupportedException("Unknown database provider: " + settings.Type + ".");
            }
        }

        #endregion
    }
}
