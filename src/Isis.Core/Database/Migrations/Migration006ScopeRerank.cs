namespace Isis.Core.Database.Migrations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Mysql;
    using Isis.Core.Database.SqlServer;

    /// <summary>
    /// Adds the scope rerank settings (rerankendpointid, rerankcandidates, rerankminscore). Uses ALTER TABLE; every
    /// existing scope takes no reranker, 20 candidates, and no minimum score. No-op on a database already in the new
    /// shape.
    /// </summary>
    internal sealed class Migration006ScopeRerank : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-24-scope-rerank";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            bool sqlServer = driver is SqlServerDatabaseDriver;
            bool mysql = driver is MysqlDatabaseDriver;
            string add = sqlServer ? "ALTER TABLE scopes ADD " : "ALTER TABLE scopes ADD COLUMN ";

            if (!await ColumnExistsAsync(driver, "rerankendpointid", token).ConfigureAwait(false))
            {
                string type = sqlServer ? "NVARCHAR(64) NULL" : (mysql ? "VARCHAR(64) NULL" : "TEXT");
                await driver.ExecuteQueryAsync(add + "rerankendpointid " + type + ";", true, token).ConfigureAwait(false);
            }

            if (!await ColumnExistsAsync(driver, "rerankcandidates", token).ConfigureAwait(false))
            {
                string type = sqlServer || mysql ? "INT NOT NULL DEFAULT 20" : "INTEGER NOT NULL DEFAULT 20";
                await driver.ExecuteQueryAsync(add + "rerankcandidates " + type + ";", true, token).ConfigureAwait(false);
            }

            if (!await ColumnExistsAsync(driver, "rerankminscore", token).ConfigureAwait(false))
            {
                string type = sqlServer ? "FLOAT NULL" : (mysql ? "DOUBLE NULL" : "REAL");
                await driver.ExecuteQueryAsync(add + "rerankminscore " + type + ";", true, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<bool> ColumnExistsAsync(DatabaseDriverBase driver, string column, CancellationToken token)
        {
            try
            {
                await driver.ExecuteQueryAsync("SELECT " + column + " FROM scopes WHERE 1 = 0;", false, token).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
