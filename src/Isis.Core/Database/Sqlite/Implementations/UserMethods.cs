namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IUserMethods"/>.
    /// </summary>
    internal class UserMethods : IUserMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite user methods.
        /// </summary>
        /// <param name="driver">The SQLite database driver.</param>
        internal UserMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<User> CreateAsync(User user, CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (String.IsNullOrEmpty(user.Id)) user.Id = IdGenerator.User();
            user.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO users (id, tenantid, firstname, lastname, email, passwordsha256, isadmin, istenantadmin, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(user.Id) + ", " +
                SqliteHelpers.ToSqlRequired(user.TenantId) + ", " +
                SqliteHelpers.ToSql(user.FirstName) + ", " +
                SqliteHelpers.ToSql(user.LastName) + ", " +
                SqliteHelpers.ToSqlRequired(user.Email) + ", " +
                SqliteHelpers.ToSql(user.PasswordSha256) + ", " +
                SqliteHelpers.ToSql(user.IsAdmin) + ", " +
                SqliteHelpers.ToSql(user.IsTenantAdmin) + ", " +
                SqliteHelpers.ToSql(user.Active) + ", " +
                SqliteHelpers.ToSql(user.Protected) + ", " +
                SqliteHelpers.ToSqlRequired(user.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(user.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return user;
        }

        /// <inheritdoc />
        public async Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM users WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM users WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND email = " + SqliteHelpers.ToSqlRequired(email) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<List<User>> EnumerateByEmailAsync(string email, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(email)) throw new ArgumentNullException(nameof(email));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM users WHERE email = " + SqliteHelpers.ToSqlRequired(email) +
                " ORDER BY createdutc ASC;", false, token).ConfigureAwait(false);

            List<User> users = new List<User>();
            foreach (DataRow row in table.Rows) users.Add(FromRow(row));
            return users;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<User>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<User> result = new EnumerationResult<User> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string term = SqliteHelpers.Sanitize(query.SearchTerm);
                where += " AND (email LIKE '%" + term + "%' OR firstname LIKE '%" + term + "%' OR lastname LIKE '%" + term + "%')";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM users" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM users").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<User> UpdateAsync(User user, CancellationToken token = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE users SET " +
                "firstname = " + SqliteHelpers.ToSql(user.FirstName) + ", " +
                "lastname = " + SqliteHelpers.ToSql(user.LastName) + ", " +
                "email = " + SqliteHelpers.ToSqlRequired(user.Email) + ", " +
                "passwordsha256 = " + SqliteHelpers.ToSql(user.PasswordSha256) + ", " +
                "isadmin = " + SqliteHelpers.ToSql(user.IsAdmin) + ", " +
                "istenantadmin = " + SqliteHelpers.ToSql(user.IsTenantAdmin) + ", " +
                "active = " + SqliteHelpers.ToSql(user.Active) + ", " +
                "isprotected = " + SqliteHelpers.ToSql(user.Protected) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(user.LastUpdateUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(user.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(user.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return user;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            User? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM users WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private static User FromRow(DataRow row)
        {
            User user = new User();
            user.Id = SqliteHelpers.GetString(row["id"]);
            user.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            user.FirstName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["firstname"]));
            user.LastName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["lastname"]));
            user.Email = SqliteHelpers.GetString(row["email"]);
            user.PasswordSha256 = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["passwordsha256"]));
            user.IsAdmin = SqliteHelpers.GetBool(row["isadmin"]);
            user.IsTenantAdmin = SqliteHelpers.GetBool(row["istenantadmin"]);
            user.Active = SqliteHelpers.GetBool(row["active"]);
            user.Protected = SqliteHelpers.GetBool(row["isprotected"]);
            user.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            user.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return user;
        }

        #endregion
    }
}
