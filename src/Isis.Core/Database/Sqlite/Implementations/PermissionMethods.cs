namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// Portable implementation of <see cref="IPermissionMethods"/>.
    /// </summary>
    internal class PermissionMethods : IPermissionMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal PermissionMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Permission> CreateAsync(Permission permission, CancellationToken token = default)
        {
            if (permission == null) throw new ArgumentNullException(nameof(permission));
            if (String.IsNullOrEmpty(permission.Id)) permission.Id = IdGenerator.Permission();
            permission.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO permissions (id, tenantid, userid, resourcetype, operation, permissiontype, resourceid, active, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(permission.Id) + ", " +
                SqliteHelpers.ToSqlRequired(permission.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(permission.UserId) + ", " +
                SqliteHelpers.ToSqlRequired(permission.ResourceType) + ", " +
                SqliteHelpers.ToSqlRequired(permission.Operation) + ", " +
                SqliteHelpers.ToSqlRequired(permission.PermissionType.ToString()) + ", " +
                SqliteHelpers.ToSql(permission.ResourceId) + ", " +
                SqliteHelpers.ToSql(permission.Active) + ", " +
                SqliteHelpers.ToSqlRequired(permission.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(permission.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return permission;
        }

        /// <inheritdoc />
        public async Task<Permission?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM permissions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<Permission>> ListForUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            List<Permission> permissions = new List<Permission>();
            if (String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(userId)) return permissions;

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM permissions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND userid = " + SqliteHelpers.ToSqlRequired(userId) +
                " AND active = 1;", false, token).ConfigureAwait(false);

            foreach (DataRow row in table.Rows) permissions.Add(FromRow(row));
            return permissions;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Permission>> EnumerateAsync(string tenantId, string? userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Permission> result = new EnumerationResult<Permission> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(userId)) where += " AND userid = " + SqliteHelpers.ToSqlRequired(userId);

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM permissions" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM permissions").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Permission? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM permissions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private static Permission FromRow(DataRow row)
        {
            Permission permission = new Permission();
            permission.Id = SqliteHelpers.GetString(row["id"]);
            permission.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            permission.UserId = SqliteHelpers.GetString(row["userid"]);
            permission.ResourceType = SqliteHelpers.GetString(row["resourcetype"]);
            permission.Operation = SqliteHelpers.GetString(row["operation"]);
            permission.PermissionType = Enum.TryParse(SqliteHelpers.GetString(row["permissiontype"]), out PermissionTypeEnum type) ? type : PermissionTypeEnum.Permit;
            permission.ResourceId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["resourceid"]));
            permission.Active = SqliteHelpers.GetBool(row["active"]);
            permission.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            permission.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return permission;
        }

        #endregion
    }
}
