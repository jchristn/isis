namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IScopeMethods"/>.
    /// </summary>
    internal class ScopeMethods : IScopeMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal ScopeMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Scope> CreateAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (String.IsNullOrEmpty(scope.Id)) scope.Id = IdGenerator.Scope();
            scope.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO scopes (id, tenantid, name, description, storeprovider, recallcollectionid, dimensionality, embeddingendpointid, filesystemlayout, targetpath, active, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(scope.Id) + ", " +
                SqliteHelpers.ToSqlRequired(scope.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(scope.Name) + ", " +
                SqliteHelpers.ToSql(scope.Description) + ", " +
                SqliteHelpers.ToSqlRequired(scope.StoreProvider.ToString()) + ", " +
                SqliteHelpers.ToSql(scope.RecallCollectionId) + ", " +
                scope.Dimensionality + ", " +
                SqliteHelpers.ToSql(scope.EmbeddingEndpointId) + ", " +
                SqliteHelpers.ToSqlRequired(scope.FilesystemLayout.ToString()) + ", " +
                SqliteHelpers.ToSql(scope.TargetPath) + ", " +
                SqliteHelpers.ToSql(scope.Active) + ", " +
                SqliteHelpers.ToSqlRequired(scope.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(scope.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return scope;
        }

        /// <inheritdoc />
        public async Task<Scope?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM scopes WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Scope?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM scopes WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND name = " + SqliteHelpers.ToSqlRequired(name) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Scope>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Scope> result = new EnumerationResult<Scope> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm)) where += " AND name LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'";

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM scopes" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM scopes").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Scope> UpdateAsync(Scope scope, CancellationToken token = default)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            scope.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE scopes SET " +
                "name = " + SqliteHelpers.ToSqlRequired(scope.Name) + ", " +
                "description = " + SqliteHelpers.ToSql(scope.Description) + ", " +
                "storeprovider = " + SqliteHelpers.ToSqlRequired(scope.StoreProvider.ToString()) + ", " +
                "recallcollectionid = " + SqliteHelpers.ToSql(scope.RecallCollectionId) + ", " +
                "dimensionality = " + scope.Dimensionality + ", " +
                "embeddingendpointid = " + SqliteHelpers.ToSql(scope.EmbeddingEndpointId) + ", " +
                "filesystemlayout = " + SqliteHelpers.ToSqlRequired(scope.FilesystemLayout.ToString()) + ", " +
                "targetpath = " + SqliteHelpers.ToSql(scope.TargetPath) + ", " +
                "active = " + SqliteHelpers.ToSql(scope.Active) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(scope.LastUpdateUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(scope.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(scope.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return scope;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Scope? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM scopes WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<Scope>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return new List<Scope>();

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM scopes WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", false, token).ConfigureAwait(false);

            List<Scope> results = new List<Scope>();
            foreach (DataRow row in table.Rows) results.Add(FromRow(row));
            return results;
        }

        /// <inheritdoc />
        public async Task<List<Scope>> CreateManyAsync(IReadOnlyCollection<Scope> items, CancellationToken token = default)
        {
            if (items == null || items.Count == 0) return new List<Scope>();

            List<Scope> results = new List<Scope>();
            foreach (Scope item in items) results.Add(await CreateAsync(item, token).ConfigureAwait(false));
            return results;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return 0;

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM scopes WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", true, token).ConfigureAwait(false);
            return ids.Count;
        }

        #endregion

        #region Private-Methods

        private static Scope FromRow(DataRow row)
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
            scope.Active = SqliteHelpers.GetBool(row["active"]);
            scope.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            scope.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return scope;
        }

        #endregion
    }
}
