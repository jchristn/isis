namespace Isis.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Migrations;
    using Isis.Core.Database.Sqlite;
    using Isis.Core.Helpers;

    /// <summary>
    /// Applies ordered schema migrations that the create-only DDL cannot express. Each migration runs once, in
    /// registry order, after the base schema has been created; the runner records applied migrations by name in
    /// the <c>schemamigrations</c> table and skips any already recorded. Provider-neutral — a single
    /// implementation serves every driver.
    /// </summary>
    public static class MigrationRunner
    {
        #region Private-Members

        // Ordered list of migrations. Append new migrations to the end; never reorder or rename shipped ones.
        private static readonly ISchemaMigration[] _Migrations = new ISchemaMigration[]
        {
            new Migration001ModelEndpointBaseUrlAuth(),
            new Migration002InstructionScope(),
            new Migration003ScopeChunkConfig(),
            new Migration004EndpointMaxInputTokens()
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply every not-yet-applied migration in order. Call after the base schema has been created (so the
        /// <c>schemamigrations</c> table exists).
        /// </summary>
        /// <param name="driver">The database driver.</param>
        /// <param name="ensureSchema">A delegate that recreates any missing tables/indices in the latest shape
        /// (the same idempotent table creation the driver runs at startup); migrations that rebuild a table call
        /// it after dropping.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task ApplyAllAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token = default)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (ensureSchema == null) throw new ArgumentNullException(nameof(ensureSchema));

            HashSet<string> applied = await AppliedNamesAsync(driver, token).ConfigureAwait(false);

            foreach (ISchemaMigration migration in _Migrations)
            {
                if (applied.Contains(migration.Name)) continue;
                await migration.ApplyAsync(driver, ensureSchema, token).ConfigureAwait(false);
                await RecordAsync(driver, migration.Name, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<HashSet<string>> AppliedNamesAsync(DatabaseDriverBase driver, CancellationToken token)
        {
            HashSet<string> applied = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                DataTable table = await driver.ExecuteQueryAsync("SELECT name FROM schemamigrations;", false, token).ConfigureAwait(false);
                foreach (DataRow row in table.Rows)
                {
                    string name = SqliteHelpers.GetString(row["name"]);
                    if (!String.IsNullOrEmpty(name)) applied.Add(name);
                }
            }
            catch
            {
                // No schemamigrations table yet (should not happen post-create) — treat as none applied.
            }

            return applied;
        }

        private static async Task RecordAsync(DatabaseDriverBase driver, string name, CancellationToken token)
        {
            string id = SqliteHelpers.ToSqlRequired(IdGenerator.Token());
            string migrationName = SqliteHelpers.ToSqlRequired(name);
            string appliedUtc = SqliteHelpers.ToSqlRequired(DateTime.UtcNow);
            await driver.ExecuteQueryAsync(
                "INSERT INTO schemamigrations (id, name, appliedutc, success) VALUES (" + id + ", " + migrationName + ", " + appliedUtc + ", 1);",
                true, token).ConfigureAwait(false);
        }

        #endregion
    }
}
