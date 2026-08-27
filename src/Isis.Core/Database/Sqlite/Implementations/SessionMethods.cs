namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="ISessionMethods"/>.
    /// </summary>
    internal class SessionMethods : ISessionMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite session methods.
        /// </summary>
        /// <param name="driver">The SQLite database driver.</param>
        internal SessionMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (String.IsNullOrEmpty(session.Id)) session.Id = IdGenerator.Session();
            session.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO authsessions (id, tenantid, userid, credentialid, principaltype, authscheme, token, sourceip, useragent, issuedutc, expirationutc, lastusedutc, revokedutc, revocationreason, active, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(session.Id) + ", " +
                SqliteHelpers.ToSqlRequired(session.TenantId) + ", " +
                SqliteHelpers.ToSql(session.UserId) + ", " +
                SqliteHelpers.ToSql(session.CredentialId) + ", " +
                SqliteHelpers.ToSqlRequired(session.PrincipalType.ToString()) + ", " +
                SqliteHelpers.ToSqlRequired(session.AuthScheme.ToString()) + ", " +
                SqliteHelpers.ToSqlRequired(session.Token) + ", " +
                SqliteHelpers.ToSql(session.SourceIp) + ", " +
                SqliteHelpers.ToSql(session.UserAgent) + ", " +
                SqliteHelpers.ToSqlRequired(session.IssuedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(session.ExpirationUtc) + ", " +
                SqliteHelpers.ToSql(session.LastUsedUtc) + ", " +
                SqliteHelpers.ToSql(session.RevokedUtc) + ", " +
                SqliteHelpers.ToSql(session.RevocationReason) + ", " +
                SqliteHelpers.ToSql(session.Active) + ", " +
                SqliteHelpers.ToSqlRequired(session.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(session.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return session;
        }

        /// <inheritdoc />
        public async Task<AuthSession?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM authsessions WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<AuthSession?> ReadByTokenAsync(string tokenValue, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tokenValue)) throw new ArgumentNullException(nameof(tokenValue));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM authsessions WHERE token = " + SqliteHelpers.ToSqlRequired(tokenValue) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<AuthSession>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<AuthSession> result = new EnumerationResult<AuthSession> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm)) where += " AND token LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'";

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM authsessions" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM authsessions").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<AuthSession> UpdateAsync(AuthSession session, CancellationToken token = default)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE authsessions SET " +
                "userid = " + SqliteHelpers.ToSql(session.UserId) + ", " +
                "credentialid = " + SqliteHelpers.ToSql(session.CredentialId) + ", " +
                "principaltype = " + SqliteHelpers.ToSqlRequired(session.PrincipalType.ToString()) + ", " +
                "authscheme = " + SqliteHelpers.ToSqlRequired(session.AuthScheme.ToString()) + ", " +
                "token = " + SqliteHelpers.ToSqlRequired(session.Token) + ", " +
                "sourceip = " + SqliteHelpers.ToSql(session.SourceIp) + ", " +
                "useragent = " + SqliteHelpers.ToSql(session.UserAgent) + ", " +
                "issuedutc = " + SqliteHelpers.ToSqlRequired(session.IssuedUtc) + ", " +
                "expirationutc = " + SqliteHelpers.ToSqlRequired(session.ExpirationUtc) + ", " +
                "lastusedutc = " + SqliteHelpers.ToSql(session.LastUsedUtc) + ", " +
                "revokedutc = " + SqliteHelpers.ToSql(session.RevokedUtc) + ", " +
                "revocationreason = " + SqliteHelpers.ToSql(session.RevocationReason) + ", " +
                "active = " + SqliteHelpers.ToSql(session.Active) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(session.LastUpdateUtc) + " " +
                "WHERE id = " + SqliteHelpers.ToSqlRequired(session.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return session;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            AuthSession? existing = await ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync("DELETE FROM authsessions WHERE id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private static AuthSession FromRow(DataRow row)
        {
            AuthSession session = new AuthSession();
            session.Id = SqliteHelpers.GetString(row["id"]);
            session.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            session.UserId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["userid"]));
            session.CredentialId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["credentialid"]));
            session.PrincipalType = Enum.TryParse(SqliteHelpers.GetString(row["principaltype"]), out PrincipalTypeEnum principalType) ? principalType : PrincipalTypeEnum.User;
            session.AuthScheme = Enum.TryParse(SqliteHelpers.GetString(row["authscheme"]), out AuthSchemeEnum authScheme) ? authScheme : AuthSchemeEnum.BearerToken;
            session.Token = SqliteHelpers.GetString(row["token"]);
            session.SourceIp = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["sourceip"]));
            session.UserAgent = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["useragent"]));
            session.IssuedUtc = SqliteHelpers.ParseTimestamp(row["issuedutc"]);
            session.ExpirationUtc = SqliteHelpers.ParseTimestamp(row["expirationutc"]);
            session.LastUsedUtc = SqliteHelpers.ParseNullableTimestamp(row["lastusedutc"]);
            session.RevokedUtc = SqliteHelpers.ParseNullableTimestamp(row["revokedutc"]);
            session.RevocationReason = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["revocationreason"]));
            session.Active = SqliteHelpers.GetBool(row["active"]);
            session.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            session.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return session;
        }

        #endregion
    }
}
