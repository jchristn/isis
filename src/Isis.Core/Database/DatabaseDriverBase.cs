namespace Isis.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Interfaces;

    /// <summary>
    /// Abstract base class for Isis relational database drivers. Concrete drivers implement the raw
    /// query executors and assign the per-entity method interface implementations in their constructor.
    /// </summary>
    public abstract class DatabaseDriverBase : IDisposable, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Tenant data access methods.
        /// </summary>
        public ITenantMethods Tenants { get; protected set; } = null!;

        /// <summary>
        /// User data access methods.
        /// </summary>
        public IUserMethods Users { get; protected set; } = null!;

        /// <summary>
        /// Credential data access methods.
        /// </summary>
        public ICredentialMethods Credentials { get; protected set; } = null!;

        /// <summary>
        /// Authentication session data access methods.
        /// </summary>
        public ISessionMethods Sessions { get; protected set; } = null!;

        /// <summary>
        /// Scope data access methods.
        /// </summary>
        public IScopeMethods Scopes { get; protected set; } = null!;

        /// <summary>
        /// Category data access methods.
        /// </summary>
        public ICategoryMethods Categories { get; protected set; } = null!;

        /// <summary>
        /// Memory index data access methods.
        /// </summary>
        public IMemoryIndexMethods Memories { get; protected set; } = null!;

        /// <summary>
        /// Model endpoint data access methods.
        /// </summary>
        public IModelEndpointMethods ModelEndpoints { get; protected set; } = null!;

        /// <summary>
        /// Request history data access methods.
        /// </summary>
        public IRequestHistoryMethods RequestHistory { get; protected set; } = null!;

        /// <summary>
        /// Permission data access methods.
        /// </summary>
        public IPermissionMethods Permissions { get; protected set; } = null!;

        /// <summary>
        /// Tenant-scoped agent instruction data access methods.
        /// </summary>
        public IInstructionMethods Instructions { get; protected set; } = null!;

        /// <summary>
        /// The settings used to construct this driver.
        /// </summary>
        public DatabaseSettings Settings { get; protected set; }

        /// <summary>
        /// Optional action invoked with each executed query when query logging is enabled.
        /// </summary>
        public Action<string>? LogQueryAction { get; set; } = null;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the base driver.
        /// </summary>
        /// <param name="settings">The database settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        protected DatabaseDriverBase(DatabaseSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database: create schema and apply migrations. Safe to call repeatedly.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public abstract Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Execute a single SQL statement and return the resulting rows as a table of string values.
        /// </summary>
        /// <param name="query">The SQL statement.</param>
        /// <param name="isWrite">When true, the statement is executed within a transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result table.</returns>
        public abstract Task<DataTable> ExecuteQueryAsync(string query, bool isWrite = false, CancellationToken token = default);

        /// <summary>
        /// Execute multiple SQL statements within a single transaction.
        /// </summary>
        /// <param name="queries">The SQL statements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result table of the final statement.</returns>
        public abstract Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, CancellationToken token = default);

        /// <summary>
        /// Build the provider-specific pagination clause for an ordered query. The default is
        /// <c>LIMIT n OFFSET m</c>; providers that require a different form (for example SQL Server's
        /// <c>OFFSET .. ROWS FETCH NEXT .. ROWS ONLY</c>) override this.
        /// </summary>
        /// <param name="maxResults">The maximum number of rows.</param>
        /// <param name="skip">The number of rows to skip.</param>
        /// <returns>The pagination clause, beginning with a leading space.</returns>
        public virtual string PaginationClause(int maxResults, int skip)
        {
            return " LIMIT " + maxResults + " OFFSET " + skip;
        }

        /// <summary>
        /// Test database connectivity.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the database responds.</returns>
        public virtual async Task<bool> PingAsync(CancellationToken token = default)
        {
            await ExecuteQueryAsync("SELECT 1;", false, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Dispose the driver.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously dispose the driver.
        /// </summary>
        /// <returns>Value task.</returns>
        public virtual async ValueTask DisposeAsync()
        {
            if (!_Disposed)
            {
                Dispose(true);
                _Disposed = true;
            }

            await Task.CompletedTask.ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            _Disposed = true;
        }

        /// <summary>
        /// Invoke the query-log action when configured.
        /// </summary>
        /// <param name="query">The query being executed.</param>
        protected void LogQuery(string query)
        {
            if (Settings.LogQueries && LogQueryAction != null) LogQueryAction.Invoke(query);
        }

        #endregion
    }
}
