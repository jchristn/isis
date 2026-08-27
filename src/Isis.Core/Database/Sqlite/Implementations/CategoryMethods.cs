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
    /// SQLite implementation of <see cref="ICategoryMethods"/>.
    /// </summary>
    internal class CategoryMethods : ICategoryMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQLite category methods.
        /// </summary>
        /// <param name="driver">The SQLite database driver.</param>
        internal CategoryMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Category> CreateAsync(Category category, CancellationToken token = default)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (String.IsNullOrEmpty(category.Id)) category.Id = IdGenerator.Category();
            category.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO categories (id, tenantid, scopeid, name, description, instructions, active, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(category.Id) + ", " +
                SqliteHelpers.ToSqlRequired(category.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(category.ScopeId) + ", " +
                SqliteHelpers.ToSqlRequired(category.Name) + ", " +
                SqliteHelpers.ToSql(category.Description) + ", " +
                SqliteHelpers.ToSql(category.Instructions) + ", " +
                SqliteHelpers.ToSql(category.Active) + ", " +
                SqliteHelpers.ToSqlRequired(category.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(category.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return category;
        }

        /// <inheritdoc />
        public async Task<Category?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM categories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Category?> ReadByNameAsync(string tenantId, string scopeId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(scopeId)) throw new ArgumentNullException(nameof(scopeId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM categories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND scopeid = " + SqliteHelpers.ToSqlRequired(scopeId) +
                " AND name = " + SqliteHelpers.ToSqlRequired(name) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Category>> EnumerateAsync(string tenantId, string scopeId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(scopeId)) throw new ArgumentNullException(nameof(scopeId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Category> result = new EnumerationResult<Category> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND scopeid = " + SqliteHelpers.ToSqlRequired(scopeId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string term = SqliteHelpers.Sanitize(query.SearchTerm);
                where += " AND (name LIKE '%" + term + "%' OR description LIKE '%" + term + "%')";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM categories" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM categories").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Category> UpdateAsync(Category category, CancellationToken token = default)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            category.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE categories SET " +
                "name = " + SqliteHelpers.ToSqlRequired(category.Name) + ", " +
                "description = " + SqliteHelpers.ToSql(category.Description) + ", " +
                "instructions = " + SqliteHelpers.ToSql(category.Instructions) + ", " +
                "active = " + SqliteHelpers.ToSql(category.Active) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(category.LastUpdateUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(category.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(category.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return category;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Category? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM categories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Private-Methods

        private static Category FromRow(DataRow row)
        {
            Category category = new Category();
            category.Id = SqliteHelpers.GetString(row["id"]);
            category.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            category.ScopeId = SqliteHelpers.GetString(row["scopeid"]);
            category.Name = SqliteHelpers.GetString(row["name"]);
            category.Description = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["description"]));
            category.Instructions = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["instructions"]));
            category.Active = SqliteHelpers.GetBool(row["active"]);
            category.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            category.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return category;
        }

        #endregion
    }
}
