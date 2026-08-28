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
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IInstructionMethods"/>.
    /// </summary>
    internal class InstructionMethods : IInstructionMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal InstructionMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Instruction> CreateAsync(Instruction instruction, CancellationToken token = default)
        {
            if (instruction == null) throw new ArgumentNullException(nameof(instruction));
            if (String.IsNullOrEmpty(instruction.Id)) instruction.Id = IdGenerator.Instruction();
            instruction.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO instructions (id, tenantid, name, content, position, active, isprotected, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(instruction.Id) + ", " +
                SqliteHelpers.ToSqlRequired(instruction.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(instruction.Name) + ", " +
                SqliteHelpers.ToSql(instruction.Content) + ", " +
                instruction.Position + ", " +
                SqliteHelpers.ToSql(instruction.Active) + ", " +
                SqliteHelpers.ToSql(instruction.Protected) + ", " +
                SqliteHelpers.ToSqlRequired(instruction.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(instruction.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return instruction;
        }

        /// <inheritdoc />
        public async Task<Instruction?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM instructions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Instruction>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<Instruction> result = new EnumerationResult<Instruction> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (!String.IsNullOrEmpty(query.SearchTerm))
            {
                string term = SqliteHelpers.Sanitize(query.SearchTerm);
                where += " AND (name LIKE '%" + term + "%' OR content LIKE '%" + term + "%')";
            }

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM instructions" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM instructions").Append(where)
               .Append(" ORDER BY position ASC, createdutc ASC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<Instruction> UpdateAsync(Instruction instruction, CancellationToken token = default)
        {
            if (instruction == null) throw new ArgumentNullException(nameof(instruction));
            instruction.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE instructions SET " +
                "name = " + SqliteHelpers.ToSqlRequired(instruction.Name) + ", " +
                "content = " + SqliteHelpers.ToSql(instruction.Content) + ", " +
                "position = " + instruction.Position + ", " +
                "active = " + SqliteHelpers.ToSql(instruction.Active) + ", " +
                "isprotected = " + SqliteHelpers.ToSql(instruction.Protected) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(instruction.LastUpdateUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(instruction.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(instruction.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return instruction;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Instruction? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM instructions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<Instruction>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return new List<Instruction>();

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM instructions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", false, token).ConfigureAwait(false);

            List<Instruction> results = new List<Instruction>();
            foreach (DataRow row in table.Rows) results.Add(FromRow(row));
            return results;
        }

        /// <inheritdoc />
        public async Task<List<Instruction>> CreateManyAsync(IReadOnlyCollection<Instruction> items, CancellationToken token = default)
        {
            if (items == null || items.Count == 0) return new List<Instruction>();

            List<Instruction> results = new List<Instruction>();
            foreach (Instruction item in items) results.Add(await CreateAsync(item, token).ConfigureAwait(false));
            return results;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return 0;

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM instructions WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", true, token).ConfigureAwait(false);
            return ids.Count;
        }

        #endregion

        #region Private-Methods

        private static Instruction FromRow(DataRow row)
        {
            Instruction instruction = new Instruction();
            instruction.Id = SqliteHelpers.GetString(row["id"]);
            instruction.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            instruction.Name = SqliteHelpers.GetString(row["name"]);
            instruction.Content = SqliteHelpers.GetString(row["content"]);
            instruction.Position = SqliteHelpers.GetInt(row["position"]);
            instruction.Active = SqliteHelpers.GetBool(row["active"]);
            instruction.Protected = SqliteHelpers.GetBool(row["isprotected"]);
            instruction.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            instruction.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return instruction;
        }

        #endregion
    }
}
