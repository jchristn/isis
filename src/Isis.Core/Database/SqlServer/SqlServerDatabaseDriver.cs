namespace Isis.Core.Database.SqlServer
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Sqlite.Implementations;
    using Isis.Core.Database.SqlServer.Queries;
    using Microsoft.Data.SqlClient;

    /// <summary>
    /// SQL Server implementation of the Isis database driver. Reuses the portable entity method
    /// implementations, executes the SQL Server-specific schema, and overrides pagination to use
    /// OFFSET/FETCH.
    /// </summary>
    public class SqlServerDatabaseDriver : DatabaseDriverBase
    {
        #region Private-Members

        private readonly string _ConnectionString;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the SQL Server driver.
        /// </summary>
        /// <param name="settings">The database settings.</param>
        /// <exception cref="ArgumentException">Thrown when required connection settings are missing.</exception>
        public SqlServerDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            if (string.IsNullOrEmpty(settings.Hostname)) throw new ArgumentException("A hostname is required for the SQL Server provider.", nameof(settings));
            if (string.IsNullOrEmpty(settings.DatabaseName)) throw new ArgumentException("A database name is required for the SQL Server provider.", nameof(settings));

            _ConnectionString = BuildConnectionString(settings);

            Tenants = new TenantMethods(this);
            Users = new UserMethods(this);
            Credentials = new CredentialMethods(this);
            Sessions = new SessionMethods(this);
            Scopes = new ScopeMethods(this);
            Categories = new CategoryMethods(this);
            Memories = new MemoryIndexMethods(this);
            ModelEndpoints = new ModelEndpointMethods(this);
            RequestHistory = new RequestHistoryMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string PaginationClause(int maxResults, int skip)
        {
            return " OFFSET " + skip + " ROWS FETCH NEXT " + maxResults + " ROWS ONLY";
        }

        /// <inheritdoc />
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            foreach (string statement in SplitStatements(SqlServerSetupQueries.CreateTables()))
            {
                await ExecuteQueryAsync(statement, true, token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueryAsync(string query, bool isWrite = false, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(query)) throw new ArgumentNullException(nameof(query));

            token.ThrowIfCancellationRequested();
            LogQuery(query);

            DataTable result = new DataTable();

            await using SqlConnection connection = new SqlConnection(_ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);

            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = query;

                await using SqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                do
                {
                    if (reader.FieldCount == 0) continue;

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
                            row[i] = reader.IsDBNull(i) ? string.Empty : (reader.GetValue(i)?.ToString() ?? string.Empty);
                        }

                        result.Rows.Add(row);
                    }
                }
                while (await reader.NextResultAsync(token).ConfigureAwait(false));
            }

            await connection.CloseAsync().ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, CancellationToken token = default)
        {
            if (queries == null) throw new ArgumentNullException(nameof(queries));

            token.ThrowIfCancellationRequested();

            await using SqlConnection connection = new SqlConnection(_ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);

            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                foreach (string query in queries)
                {
                    if (string.IsNullOrEmpty(query)) continue;
                    LogQuery(query);

                    await using SqlCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = query;
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(token).ConfigureAwait(false);
                throw;
            }

            await connection.CloseAsync().ConfigureAwait(false);
            return new DataTable();
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<string> SplitStatements(string script)
        {
            foreach (string part in script.Split(';'))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed)) yield return trimmed;
            }
        }

        private static string BuildConnectionString(DatabaseSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            int port = settings.GetEffectivePort();
            sb.Append("Server=").Append(settings.Hostname);
            if (port > 0) sb.Append(',').Append(port);
            sb.Append(';');
            sb.Append("Database=").Append(settings.DatabaseName).Append(';');
            if (!string.IsNullOrEmpty(settings.Username)) sb.Append("User Id=").Append(settings.Username).Append(';');
            if (!string.IsNullOrEmpty(settings.Password)) sb.Append("Password=").Append(settings.Password).Append(';');
            sb.Append(settings.RequireEncryption ? "Encrypt=True;" : "Encrypt=False;");
            sb.Append("TrustServerCertificate=True;");
            return sb.ToString();
        }

        #endregion
    }
}
