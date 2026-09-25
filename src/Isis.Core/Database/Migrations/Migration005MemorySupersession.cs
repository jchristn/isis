namespace Isis.Core.Database.Migrations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Mysql;
    using Isis.Core.Database.SqlServer;

    /// <summary>
    /// Adds memory supersession columns (supersedes: JSON list of replaced slugs; supersededby: id of the replacing
    /// memory). Uses ALTER TABLE rather than a rebuild, because the memories table can be large and every existing
    /// row simply takes null. No-op on a database already in the new shape.
    /// </summary>
    internal sealed class Migration005MemorySupersession : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-24-memory-supersession";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            if (!await ColumnExistsAsync(driver, "supersedes", token).ConfigureAwait(false))
            {
                await driver.ExecuteQueryAsync(AddColumn(driver, "supersedes", true), true, token).ConfigureAwait(false);
            }

            if (!await ColumnExistsAsync(driver, "supersededby", token).ConfigureAwait(false))
            {
                await driver.ExecuteQueryAsync(AddColumn(driver, "supersededby", false), true, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<bool> ColumnExistsAsync(DatabaseDriverBase driver, string column, CancellationToken token)
        {
            try
            {
                await driver.ExecuteQueryAsync("SELECT " + column + " FROM memories WHERE 1 = 0;", false, token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string AddColumn(DatabaseDriverBase driver, string column, bool isLongText)
        {
            if (driver is SqlServerDatabaseDriver) return "ALTER TABLE memories ADD " + column + " " + (isLongText ? "NVARCHAR(MAX)" : "NVARCHAR(64)") + " NULL;";
            if (driver is MysqlDatabaseDriver) return "ALTER TABLE memories ADD COLUMN " + column + " " + (isLongText ? "LONGTEXT" : "VARCHAR(64)") + " NULL;";
            return "ALTER TABLE memories ADD COLUMN " + column + " TEXT;";
        }

        #endregion
    }
}
