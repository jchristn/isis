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
    /// Portable implementation of <see cref="IOperationEventMethods"/>.
    /// </summary>
    internal class OperationEventMethods : IOperationEventMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal OperationEventMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<OperationEvent> CreateAsync(OperationEvent entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(entry.Id)) entry.Id = IdGenerator.Operation();

            string query =
                "INSERT INTO operation_events (id, tenantid, resourcetype, operation, resourceid, scopeid, method, path, statuscode, principalname, sourceip, durationms, createdutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(entry.Id) + ", " +
                SqliteHelpers.ToSql(entry.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(entry.ResourceType) + ", " +
                SqliteHelpers.ToSqlRequired(entry.Operation) + ", " +
                SqliteHelpers.ToSql(entry.ResourceId) + ", " +
                SqliteHelpers.ToSql(entry.ScopeId) + ", " +
                SqliteHelpers.ToSqlRequired(entry.Method) + ", " +
                SqliteHelpers.ToSqlRequired(entry.Path) + ", " +
                entry.StatusCode + ", " +
                SqliteHelpers.ToSql(entry.PrincipalName) + ", " +
                SqliteHelpers.ToSql(entry.SourceIp) + ", " +
                entry.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                SqliteHelpers.ToSqlRequired(entry.CreatedUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return entry;
        }

        /// <inheritdoc />
        public async Task<OperationEvent?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM operation_events WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<OperationEvent>> EnumerateAsync(string? tenantId, string? resourceType, string? operation, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<OperationEvent> result = new EnumerationResult<OperationEvent> { MaxResults = query.MaxResults, Skip = query.Skip };

            StringBuilder where = new StringBuilder();
            AppendClause(where, !String.IsNullOrEmpty(tenantId), "tenantid = " + SqliteHelpers.ToSqlRequired(tenantId));
            AppendClause(where, !String.IsNullOrEmpty(resourceType), "resourcetype = " + SqliteHelpers.ToSqlRequired(resourceType));
            AppendClause(where, !String.IsNullOrEmpty(operation), "operation = " + SqliteHelpers.ToSqlRequired(operation));
            AppendClause(where, !String.IsNullOrEmpty(query.SearchTerm), "path LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'");

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM operation_events" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM operation_events").Append(where)
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

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM operation_events" + where + ";", false, token).ConfigureAwait(false);
            long count = countTable.Rows.Count > 0 ? SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]) : 0;

            await _Driver.ExecuteQueryAsync("DELETE FROM operation_events" + where + ";", true, token).ConfigureAwait(false);
            return count;
        }

        /// <inheritdoc />
        public async Task<long> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            string cutoff = SqliteHelpers.ToSqlRequired(cutoffUtc);

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM operation_events WHERE createdutc < " + cutoff + ";", false, token).ConfigureAwait(false);
            long count = countTable.Rows.Count > 0 ? SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]) : 0;

            await _Driver.ExecuteQueryAsync("DELETE FROM operation_events WHERE createdutc < " + cutoff + ";", true, token).ConfigureAwait(false);
            return count;
        }

        #endregion

        #region Private-Methods

        private static void AppendClause(StringBuilder where, bool include, string clause)
        {
            if (!include) return;
            where.Append(where.Length == 0 ? " WHERE " : " AND ").Append(clause);
        }

        private static OperationEvent FromRow(DataRow row)
        {
            OperationEvent entry = new OperationEvent();
            entry.Id = SqliteHelpers.GetString(row["id"]);
            entry.TenantId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["tenantid"]));
            entry.ResourceType = SqliteHelpers.GetString(row["resourcetype"]);
            entry.Operation = SqliteHelpers.GetString(row["operation"]);
            entry.ResourceId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["resourceid"]));
            entry.ScopeId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["scopeid"]));
            entry.Method = SqliteHelpers.GetString(row["method"]);
            entry.Path = SqliteHelpers.GetString(row["path"]);
            entry.StatusCode = SqliteHelpers.GetInt(row["statuscode"]);
            entry.PrincipalName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["principalname"]));
            entry.SourceIp = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["sourceip"]));
            entry.DurationMs = SqliteHelpers.GetDouble(row["durationms"]);
            entry.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            return entry;
        }

        #endregion
    }
}
