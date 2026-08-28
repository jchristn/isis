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
    /// SQLite implementation of <see cref="ICredentialMethods"/>.
    /// </summary>
    internal class CredentialMethods : ICredentialMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite credential methods.
        /// </summary>
        /// <param name="driver">The SQLite database driver.</param>
        internal CredentialMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Credential> CreateAsync(Credential credential, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (String.IsNullOrEmpty(credential.Id)) credential.Id = IdGenerator.Credential();
            credential.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO credentials (id, tenantid, userid, name, accesskey, secretkey, authmode, active, isprotected, createdutc, lastupdateutc, lastusedutc, expirationutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(credential.Id) + ", " +
                SqliteHelpers.ToSqlRequired(credential.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(credential.UserId) + ", " +
                SqliteHelpers.ToSql(credential.Name) + ", " +
                SqliteHelpers.ToSqlRequired(credential.AccessKey) + ", " +
                SqliteHelpers.ToSql(credential.SecretKey) + ", " +
                SqliteHelpers.ToSqlRequired(credential.AuthMode.ToString()) + ", " +
                SqliteHelpers.ToSql(credential.Active) + ", " +
                SqliteHelpers.ToSql(credential.Protected) + ", " +
                SqliteHelpers.ToSqlRequired(credential.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(credential.LastUpdateUtc) + ", " +
                SqliteHelpers.ToSql(credential.LastUsedUtc) + ", " +
                SqliteHelpers.ToSql(credential.ExpirationUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return credential;
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM credentials WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Credential?> ReadByAccessKeyAsync(string accessKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM credentials WHERE accesskey = " + SqliteHelpers.ToSqlRequired(accessKey) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Credential>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Credential> result = new EnumerationResult<Credential> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string term = SqliteHelpers.Sanitize(query.SearchTerm);
                where += " AND (name LIKE '%" + term + "%' OR accesskey LIKE '%" + term + "%')";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM credentials" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM credentials").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Credential> UpdateAsync(Credential credential, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            credential.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE credentials SET " +
                "userid = " + SqliteHelpers.ToSqlRequired(credential.UserId) + ", " +
                "name = " + SqliteHelpers.ToSql(credential.Name) + ", " +
                "accesskey = " + SqliteHelpers.ToSqlRequired(credential.AccessKey) + ", " +
                "secretkey = " + SqliteHelpers.ToSql(credential.SecretKey) + ", " +
                "authmode = " + SqliteHelpers.ToSqlRequired(credential.AuthMode.ToString()) + ", " +
                "active = " + SqliteHelpers.ToSql(credential.Active) + ", " +
                "isprotected = " + SqliteHelpers.ToSql(credential.Protected) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(credential.LastUpdateUtc) + ", " +
                "lastusedutc = " + SqliteHelpers.ToSql(credential.LastUsedUtc) + ", " +
                "expirationutc = " + SqliteHelpers.ToSql(credential.ExpirationUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(credential.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(credential.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return credential;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Credential? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM credentials WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<Credential>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return new List<Credential>();

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM credentials WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", false, token).ConfigureAwait(false);

            List<Credential> results = new List<Credential>();
            foreach (DataRow row in table.Rows) results.Add(FromRow(row));
            return results;
        }

        /// <inheritdoc />
        public async Task<List<Credential>> CreateManyAsync(IReadOnlyCollection<Credential> items, CancellationToken token = default)
        {
            if (items == null || items.Count == 0) return new List<Credential>();

            List<Credential> results = new List<Credential>();
            foreach (Credential item in items) results.Add(await CreateAsync(item, token).ConfigureAwait(false));
            return results;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return 0;

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM credentials WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", true, token).ConfigureAwait(false);
            return ids.Count;
        }

        #endregion

        #region Private-Methods

        private static Credential FromRow(DataRow row)
        {
            Credential credential = new Credential();
            credential.Id = SqliteHelpers.GetString(row["id"]);
            credential.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            credential.UserId = SqliteHelpers.GetString(row["userid"]);
            credential.Name = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["name"]));
            credential.AccessKey = SqliteHelpers.GetString(row["accesskey"]);
            credential.SecretKey = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["secretkey"]));
            credential.AuthMode = Enum.TryParse(SqliteHelpers.GetString(row["authmode"]), out CredentialAuthModeEnum authMode) ? authMode : CredentialAuthModeEnum.DirectHeader;
            credential.Active = SqliteHelpers.GetBool(row["active"]);
            credential.Protected = SqliteHelpers.GetBool(row["isprotected"]);
            credential.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            credential.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            credential.LastUsedUtc = SqliteHelpers.ParseNullableTimestamp(row["lastusedutc"]);
            credential.ExpirationUtc = SqliteHelpers.ParseNullableTimestamp(row["expirationutc"]);
            return credential;
        }

        #endregion
    }
}
