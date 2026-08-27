namespace Isis.Core.Enums
{
    /// <summary>
    /// Supported relational database providers for the Isis metadata store.
    /// </summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// SQLite (default; single-file, suitable for local development).
        /// </summary>
        Sqlite,

        /// <summary>
        /// MySQL.
        /// </summary>
        Mysql,

        /// <summary>
        /// PostgreSQL (recommended for shared deployments alongside RecallDB).
        /// </summary>
        Postgresql,

        /// <summary>
        /// Microsoft SQL Server.
        /// </summary>
        SqlServer
    }
}
