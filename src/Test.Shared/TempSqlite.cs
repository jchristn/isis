namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// An initialized, temporary SQLite database driver that deletes its backing file on disposal.
    /// </summary>
    public sealed class TempSqlite : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The database driver.
        /// </summary>
        public DatabaseDriverBase Db { get; }

        #endregion

        #region Private-Members

        private readonly string _File;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private TempSqlite(DatabaseDriverBase db, string file)
        {
            Db = db;
            _File = file;
        }

        /// <summary>
        /// Create and initialize a temporary SQLite database.
        /// </summary>
        /// <returns>The temporary database.</returns>
        public static async Task<TempSqlite> CreateAsync()
        {
            string file = Path.Combine(Path.GetTempPath(), "isis-t-" + Guid.NewGuid().ToString("N") + ".db");
            DatabaseDriverBase db = DatabaseDriverFactory.Create(new DatabaseSettings { Type = DatabaseTypeEnum.Sqlite, Filename = file });
            await db.InitializeAsync().ConfigureAwait(false);
            return new TempSqlite(db, file);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the database and delete its file.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try { Db.Dispose(); } catch (Exception) { }
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(_File)) File.Delete(_File); } catch (Exception) { }
        }

        #endregion
    }
}
