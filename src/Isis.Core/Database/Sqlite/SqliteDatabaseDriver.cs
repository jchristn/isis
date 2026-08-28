namespace Isis.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Sqlite.Implementations;
    using Isis.Core.Database.Sqlite.Queries;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite implementation of the Isis database driver. Writes are serialized with a semaphore because
    /// SQLite does not tolerate concurrent writers.
    /// </summary>
    public class SqliteDatabaseDriver : DatabaseDriverBase
    {
        #region Private-Members

        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1, 1);
        private readonly string _ConnectionString;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        static SqliteDatabaseDriver()
        {
            SQLitePCL.Batteries_V2.Init();
        }

        /// <summary>
        /// Instantiate the SQLite driver.
        /// </summary>
        /// <param name="settings">The database settings.</param>
        /// <exception cref="ArgumentException">Thrown when the filename is not specified.</exception>
        public SqliteDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            if (String.IsNullOrEmpty(settings.Filename)) throw new ArgumentException("A filename must be specified for the SQLite provider.", nameof(settings));

            _ConnectionString = "Data Source=" + settings.Filename + ";";

            Tenants = new TenantMethods(this);
            Users = new UserMethods(this);
            Credentials = new CredentialMethods(this);
            Sessions = new SessionMethods(this);
            Scopes = new ScopeMethods(this);
            Categories = new CategoryMethods(this);
            Memories = new MemoryIndexMethods(this);
            ModelEndpoints = new ModelEndpointMethods(this);
            RequestHistory = new RequestHistoryMethods(this);
            Instructions = new InstructionMethods(this);
            Permissions = new PermissionMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(Settings.Filename));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            await ExecuteQueryAsync("PRAGMA journal_mode = WAL;", false, token).ConfigureAwait(false);
            await ExecuteQueryAsync("PRAGMA synchronous = NORMAL;", false, token).ConfigureAwait(false);
            await ExecuteQueryAsync("PRAGMA foreign_keys = ON;", false, token).ConfigureAwait(false);
            await ExecuteQueryAsync(SetupQueries.CreateTables(), true, token).ConfigureAwait(false);
            await ExecuteQueryAsync(SetupQueries.CreateIndices(), true, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueryCoreAsync(string query, bool isWrite = false, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));

            token.ThrowIfCancellationRequested();
            LogQuery(query);

            DataTable result = new DataTable();
            bool lockTaken = false;

            try
            {
                if (isWrite)
                {
                    await _WriteLock.WaitAsync(token).ConfigureAwait(false);
                    lockTaken = true;
                }

                using (SqliteConnection connection = new SqliteConnection(_ConnectionString))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);

                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = query;

                        using (SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            if (result.Columns.Count == 0)
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    result.Columns.Add(reader.GetName(i), typeof(string));
                                }
                            }

                            while (await reader.ReadAsync(token).ConfigureAwait(false))
                            {
                                DataRow row = result.NewRow();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    row[i] = reader.IsDBNull(i) ? String.Empty : (reader.GetValue(i)?.ToString() ?? String.Empty);
                                }

                                result.Rows.Add(row);
                            }
                        }
                    }

                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (lockTaken) _WriteLock.Release();
            }

            return result;
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueriesCoreAsync(IEnumerable<string> queries, CancellationToken token = default)
        {
            if (queries == null) throw new ArgumentNullException(nameof(queries));

            token.ThrowIfCancellationRequested();

            DataTable result = new DataTable();

            await _WriteLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                using (SqliteConnection connection = new SqliteConnection(_ConnectionString))
                {
                    await connection.OpenAsync(token).ConfigureAwait(false);

                    using (SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        try
                        {
                            foreach (string query in queries)
                            {
                                if (String.IsNullOrEmpty(query)) continue;
                                LogQuery(query);

                                using (SqliteCommand command = connection.CreateCommand())
                                {
                                    command.Transaction = transaction;
                                    command.CommandText = query;
                                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                                }
                            }

                            await transaction.CommitAsync(token).ConfigureAwait(false);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(token).ConfigureAwait(false);
                            throw;
                        }
                    }

                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _WriteLock.Release();
            }

            return result;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing) _WriteLock.Dispose();
                _Disposed = true;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
