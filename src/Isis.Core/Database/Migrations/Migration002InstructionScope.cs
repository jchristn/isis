namespace Isis.Core.Database.Migrations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Sqlite;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Adds per-scope scoping (scopeid + mergemode) to instructions. Legacy rows are all tenant-global.
    /// No-op on a database already in the new shape.
    /// </summary>
    internal sealed class Migration002InstructionScope : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-22-instructions-scope-merge";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            // If the scopeid column already exists, the table is in the new shape; nothing to migrate.
            try
            {
                await driver.ExecuteQueryAsync("SELECT scopeid FROM instructions WHERE 1 = 0;", false, token).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Legacy column set (or missing table) handled below.
            }

            DataTable table;
            try
            {
                table = await driver.ExecuteQueryAsync(
                    "SELECT id, tenantid, name, content, position, active, isprotected, createdutc, lastupdateutc FROM instructions;", false, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            List<Instruction> migrated = new List<Instruction>();
            foreach (DataRow row in table.Rows) migrated.Add(MapLegacyRow(row));

            await driver.ExecuteQueryAsync("DROP TABLE instructions;", true, token).ConfigureAwait(false);
            await ensureSchema(token).ConfigureAwait(false);

            foreach (Instruction instruction in migrated)
            {
                await driver.Instructions.CreateAsync(instruction, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static Instruction MapLegacyRow(DataRow row)
        {
            Instruction instruction = new Instruction();
            instruction.Id = SqliteHelpers.GetString(row["id"]);
            instruction.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            instruction.ScopeId = null; // legacy instructions are all tenant-global
            instruction.MergeMode = InstructionMergeModeEnum.Append;
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
