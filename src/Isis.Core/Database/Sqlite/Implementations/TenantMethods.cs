namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="ITenantMethods"/>.
    /// </summary>
    internal class TenantMethods : ITenantMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal TenantMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            if (String.IsNullOrEmpty(tenant.Id)) tenant.Id = IdGenerator.Tenant();
            tenant.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO tenants (id, name, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(tenant.Id) + ", " +
                SqliteHelpers.ToSqlRequired(tenant.Name) + ", " +
                SqliteHelpers.ToSql(tenant.Active) + ", " +
                SqliteHelpers.ToSql(tenant.Protected) + ", " +
                SqliteHelpers.ToSqlRequired(tenant.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(tenant.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return tenant;
        }

        /// <inheritdoc />
        public async Task<Tenant?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM tenants WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Tenant>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Tenant> result = new EnumerationResult<Tenant> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = String.IsNullOrEmpty(query.SearchTerm)
                ? String.Empty
                : " WHERE name LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'";

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM tenants" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM tenants").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken token = default)
        {
            if (tenant == null) throw new ArgumentNullException(nameof(tenant));
            tenant.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE tenants SET " +
                "name = " + SqliteHelpers.ToSqlRequired(tenant.Name) + ", " +
                "active = " + SqliteHelpers.ToSql(tenant.Active) + ", " +
                "isprotected = " + SqliteHelpers.ToSql(tenant.Protected) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(tenant.LastUpdateUtc) + " " +
                "WHERE id = " + SqliteHelpers.ToSqlRequired(tenant.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return tenant;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Tenant? existing = await ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync("DELETE FROM tenants WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private static Tenant FromRow(DataRow row)
        {
            Tenant tenant = new Tenant();
            tenant.Id = SqliteHelpers.GetString(row["id"]);
            tenant.Name = SqliteHelpers.GetString(row["name"]);
            tenant.Active = SqliteHelpers.GetBool(row["active"]);
            tenant.Protected = SqliteHelpers.GetBool(row["isprotected"]);
            tenant.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            tenant.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return tenant;
        }

        #endregion
    }
}
