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
    /// SQLite implementation of <see cref="IMemoryIndexMethods"/>.
    /// </summary>
    internal class MemoryIndexMethods : IMemoryIndexMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal MemoryIndexMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Memory> CreateAsync(Memory memory, CancellationToken token = default)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (String.IsNullOrEmpty(memory.Id)) memory.Id = IdGenerator.Memory();
            memory.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO memories (id, tenantid, scopeid, categoryid, slug, storekey, title, type, summary, body, tags, links, metadata, salience, author, sessionid, model, version, createdutc, lastupdateutc, lastaccessedutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(memory.Id) + ", " +
                SqliteHelpers.ToSqlRequired(memory.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(memory.ScopeId) + ", " +
                SqliteHelpers.ToSqlRequired(memory.CategoryId) + ", " +
                SqliteHelpers.ToSqlRequired(memory.Slug) + ", " +
                SqliteHelpers.ToSql(memory.StoreKey) + ", " +
                SqliteHelpers.ToSql(memory.Title) + ", " +
                SqliteHelpers.ToSqlRequired(memory.Type.ToString()) + ", " +
                SqliteHelpers.ToSql(memory.Summary) + ", " +
                SqliteHelpers.ToSqlRequired(memory.Body) + ", " +
                SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeList(memory.Tags)) + ", " +
                SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeList(memory.Links)) + ", " +
                SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeMap(memory.Metadata)) + ", " +
                memory.Salience.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                SqliteHelpers.ToSql(memory.Author) + ", " +
                SqliteHelpers.ToSql(memory.SessionId) + ", " +
                SqliteHelpers.ToSql(memory.Model) + ", " +
                memory.Version + ", " +
                SqliteHelpers.ToSqlRequired(memory.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(memory.LastUpdateUtc) + ", " +
                SqliteHelpers.ToSql(memory.LastAccessedUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return memory;
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM memories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Memory?> ReadBySlugAsync(string tenantId, string scopeId, string categoryId, string slug, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(scopeId)) throw new ArgumentNullException(nameof(scopeId));
            if (String.IsNullOrEmpty(categoryId)) throw new ArgumentNullException(nameof(categoryId));
            if (String.IsNullOrEmpty(slug)) throw new ArgumentNullException(nameof(slug));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM memories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND scopeid = " + SqliteHelpers.ToSqlRequired(scopeId) +
                " AND categoryid = " + SqliteHelpers.ToSqlRequired(categoryId) +
                " AND slug = " + SqliteHelpers.ToSqlRequired(slug) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Memory>> EnumerateAsync(string tenantId, string scopeId, string? categoryId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(scopeId)) throw new ArgumentNullException(nameof(scopeId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Memory> result = new EnumerationResult<Memory> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                           " AND scopeid = " + SqliteHelpers.ToSqlRequired(scopeId);
            if (!String.IsNullOrEmpty(categoryId)) where += " AND categoryid = " + SqliteHelpers.ToSqlRequired(categoryId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string term = SqliteHelpers.Sanitize(query.SearchTerm);
                where += " AND (title LIKE '%" + term + "%' OR summary LIKE '%" + term + "%' OR slug LIKE '%" + term + "%')";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM memories" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM memories").Append(where)
               .Append(" ORDER BY lastupdateutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Memory> UpdateAsync(Memory memory, CancellationToken token = default)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            memory.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE memories SET " +
                "categoryid = " + SqliteHelpers.ToSqlRequired(memory.CategoryId) + ", " +
                "slug = " + SqliteHelpers.ToSqlRequired(memory.Slug) + ", " +
                "storekey = " + SqliteHelpers.ToSql(memory.StoreKey) + ", " +
                "title = " + SqliteHelpers.ToSql(memory.Title) + ", " +
                "type = " + SqliteHelpers.ToSqlRequired(memory.Type.ToString()) + ", " +
                "summary = " + SqliteHelpers.ToSql(memory.Summary) + ", " +
                "body = " + SqliteHelpers.ToSqlRequired(memory.Body) + ", " +
                "tags = " + SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeList(memory.Tags)) + ", " +
                "links = " + SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeList(memory.Links)) + ", " +
                "metadata = " + SqliteHelpers.ToSqlRequired(SqliteHelpers.SerializeMap(memory.Metadata)) + ", " +
                "salience = " + memory.Salience.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                "author = " + SqliteHelpers.ToSql(memory.Author) + ", " +
                "sessionid = " + SqliteHelpers.ToSql(memory.SessionId) + ", " +
                "model = " + SqliteHelpers.ToSql(memory.Model) + ", " +
                "version = " + memory.Version + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(memory.LastUpdateUtc) + ", " +
                "lastaccessedutc = " + SqliteHelpers.ToSql(memory.LastAccessedUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(memory.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(memory.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return memory;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Memory? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM memories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<Memory>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return new List<Memory>();

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM memories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", false, token).ConfigureAwait(false);

            List<Memory> results = new List<Memory>();
            foreach (DataRow row in table.Rows) results.Add(FromRow(row));
            return results;
        }

        /// <inheritdoc />
        public async Task<List<Memory>> CreateManyAsync(IReadOnlyCollection<Memory> items, CancellationToken token = default)
        {
            if (items == null || items.Count == 0) return new List<Memory>();

            List<Memory> results = new List<Memory>();
            foreach (Memory item in items) results.Add(await CreateAsync(item, token).ConfigureAwait(false));
            return results;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return 0;

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM memories WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", true, token).ConfigureAwait(false);
            return ids.Count;
        }

        #endregion

        #region Private-Methods

        private static Memory FromRow(DataRow row)
        {
            Memory memory = new Memory();
            memory.Id = SqliteHelpers.GetString(row["id"]);
            memory.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            memory.ScopeId = SqliteHelpers.GetString(row["scopeid"]);
            memory.CategoryId = SqliteHelpers.GetString(row["categoryid"]);
            memory.Slug = SqliteHelpers.GetString(row["slug"]);
            memory.StoreKey = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["storekey"]));
            memory.Title = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["title"]));
            memory.Type = Enum.TryParse(SqliteHelpers.GetString(row["type"]), out MemoryTypeEnum type) ? type : MemoryTypeEnum.Project;
            memory.Summary = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["summary"]));
            memory.Body = SqliteHelpers.GetString(row["body"]);
            memory.Tags = SqliteHelpers.DeserializeList(row["tags"]);
            memory.Links = SqliteHelpers.DeserializeList(row["links"]);
            memory.Metadata = SqliteHelpers.DeserializeMap(row["metadata"]);
            memory.Salience = SqliteHelpers.GetDouble(row["salience"], 0.5);
            memory.Author = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["author"]));
            memory.SessionId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["sessionid"]));
            memory.Model = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["model"]));
            memory.Version = SqliteHelpers.GetInt(row["version"], 1);
            memory.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            memory.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            memory.LastAccessedUtc = SqliteHelpers.ParseNullableTimestamp(row["lastaccessedutc"]);
            return memory;
        }

        #endregion
    }
}
