namespace Isis.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A single, ordered schema migration. Migrations run once, in registry order, after the base schema is
    /// created; the runner records each by <see cref="Name"/> in the <c>schemamigrations</c> table and skips
    /// any already recorded. A migration must be idempotent-safe: on a fresh database (already in the latest
    /// shape) it should detect that and no-op.
    /// </summary>
    public interface ISchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// The stable migration identifier recorded in <c>schemamigrations</c>. Never change it once shipped.
        /// </summary>
        string Name { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply the migration. Provider-neutral: use only standard SQL executed through the driver and the
        /// entity DALs. When a migration must change columns it may capture rows, DROP the table, invoke
        /// <paramref name="ensureSchema"/> to recreate it in the latest shape, then re-insert.
        /// </summary>
        /// <param name="driver">The database driver.</param>
        /// <param name="ensureSchema">Recreates any missing tables/indices in the latest shape (idempotent).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token);

        #endregion
    }
}
