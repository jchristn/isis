namespace Isis.Core.Database.Migrations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Sqlite;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Adds per-scope chunking configuration (chunkingmode, chunkstrategy, chunkmaxtokens, chunkoverlaptokens).
    /// Existing scopes adopt the defaults. No-op on a database already in the new shape.
    /// </summary>
    internal sealed class Migration003ScopeChunkConfig : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-23-scope-chunk-config";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            try
            {
                await driver.ExecuteQueryAsync("SELECT chunkingmode FROM scopes WHERE 1 = 0;", false, token).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Column (or table) absent — handled below.
            }

            DataTable table;
            try
            {
                table = await driver.ExecuteQueryAsync(
                    "SELECT id, tenantid, name, description, storeprovider, recallcollectionid, dimensionality, embeddingendpointid, " +
                    "filesystemlayout, targetpath, active, createdutc, lastupdateutc FROM scopes;", false, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            List<Scope> migrated = new List<Scope>();
            foreach (DataRow row in table.Rows) migrated.Add(MapRow(row));

            await driver.ExecuteQueryAsync("DROP TABLE scopes;", true, token).ConfigureAwait(false);
            await ensureSchema(token).ConfigureAwait(false);

            foreach (Scope scope in migrated)
            {
                await driver.Scopes.CreateAsync(scope, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static Scope MapRow(DataRow row)
        {
            Scope scope = new Scope();
            scope.Id = SqliteHelpers.GetString(row["id"]);
            scope.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            scope.Name = SqliteHelpers.GetString(row["name"]);
            scope.Description = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["description"]));
            scope.StoreProvider = Enum.TryParse(SqliteHelpers.GetString(row["storeprovider"]), out StoreProviderEnum provider) ? provider : StoreProviderEnum.RecallDb;
            scope.RecallCollectionId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["recallcollectionid"]));
            scope.Dimensionality = SqliteHelpers.GetInt(row["dimensionality"]);
            scope.EmbeddingEndpointId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["embeddingendpointid"]));
            scope.FilesystemLayout = Enum.TryParse(SqliteHelpers.GetString(row["filesystemlayout"]), out FilesystemLayoutEnum layout) ? layout : FilesystemLayoutEnum.Hierarchy;
            scope.TargetPath = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["targetpath"]));
            // Chunk config takes its model defaults (OnOverflow / FixedTokenCount / 0 / 64).
            scope.Active = SqliteHelpers.GetBool(row["active"]);
            scope.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            scope.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return scope;
        }

        #endregion
    }
}
