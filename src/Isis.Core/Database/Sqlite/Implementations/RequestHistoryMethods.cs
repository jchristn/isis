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
    /// Portable implementation of <see cref="IRequestHistoryMethods"/>.
    /// </summary>
    internal class RequestHistoryMethods : IRequestHistoryMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal RequestHistoryMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<RequestHistoryEntry> CreateAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(entry.Id)) entry.Id = IdGenerator.Request();

            string query =
                "INSERT INTO request_history (id, tenantid, method, path, statuscode, sourceip, principalname, durationms, createdutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(entry.Id) + ", " +
                SqliteHelpers.ToSql(entry.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(entry.Method) + ", " +
                SqliteHelpers.ToSqlRequired(entry.Path) + ", " +
                entry.StatusCode + ", " +
                SqliteHelpers.ToSql(entry.SourceIp) + ", " +
                SqliteHelpers.ToSql(entry.PrincipalName) + ", " +
                entry.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                SqliteHelpers.ToSqlRequired(entry.CreatedUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM request_history WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<RequestHistoryEntry> result = new EnumerationResult<RequestHistoryEntry> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = string.Empty;
            if (!String.IsNullOrEmpty(tenantId)) where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string clause = String.IsNullOrEmpty(where) ? " WHERE" : " AND";
                where += clause + " path LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM request_history" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM request_history").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<long> DeleteAllAsync(string? tenantId, CancellationToken token = default)
        {
            string where = String.IsNullOrEmpty(tenantId) ? string.Empty : " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM request_history" + where + ";", false, token).ConfigureAwait(false);
            long count = countTable.Rows.Count > 0 ? SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]) : 0;

            await _Driver.ExecuteQueryAsync("DELETE FROM request_history" + where + ";", true, token).ConfigureAwait(false);
            return count;
        }

        #endregion

        #region Private-Methods

        private static RequestHistoryEntry FromRow(DataRow row)
        {
            RequestHistoryEntry entry = new RequestHistoryEntry();
            entry.Id = SqliteHelpers.GetString(row["id"]);
            entry.TenantId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["tenantid"]));
            entry.Method = SqliteHelpers.GetString(row["method"]);
            entry.Path = SqliteHelpers.GetString(row["path"]);
            entry.StatusCode = SqliteHelpers.GetInt(row["statuscode"]);
            entry.SourceIp = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["sourceip"]));
            entry.PrincipalName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["principalname"]));
            entry.DurationMs = SqliteHelpers.GetDouble(row["durationms"]);
            entry.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            return entry;
        }

        #endregion
    }
}
