namespace Isis.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core;
    using Isis.Core.Database.Interfaces;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IModelEndpointMethods"/>.
    /// </summary>
    internal class ModelEndpointMethods : IModelEndpointMethods
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        internal ModelEndpointMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            string expectedPrefix = endpoint.Kind == EndpointKindEnum.Inference ? Constants.InferenceEndpointPrefix : Constants.EmbeddingEndpointPrefix;
            if (String.IsNullOrEmpty(endpoint.Id) || !endpoint.Id.StartsWith(expectedPrefix, StringComparison.Ordinal)) endpoint.Id = IdGenerator.Endpoint(endpoint.Kind);
            endpoint.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "INSERT INTO model_endpoints (id, tenantid, name, kind, apiformat, hostname, port, usessl, apikey, model, dimensionality, timeoutms, active, healthcheckurl, healthcheckmethod, healthcheckintervalms, healthchecktimeoutms, healthcheckexpectedstatuscode, healthythreshold, unhealthythreshold, healthcheckuseauth, createdutc, lastupdateutc) VALUES (" +
                SqliteHelpers.ToSqlRequired(endpoint.Id) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.TenantId) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.Name) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.Kind.ToString()) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.ApiFormat.ToString()) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.Hostname) + ", " +
                endpoint.Port + ", " +
                SqliteHelpers.ToSql(endpoint.UseSsl) + ", " +
                SqliteHelpers.ToSql(endpoint.ApiKey) + ", " +
                SqliteHelpers.ToSql(endpoint.Model) + ", " +
                endpoint.Dimensionality + ", " +
                endpoint.TimeoutMs + ", " +
                SqliteHelpers.ToSql(endpoint.Active) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.HealthCheckUrl) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.HealthCheckMethod.ToString()) + ", " +
                endpoint.HealthCheckIntervalMs + ", " +
                endpoint.HealthCheckTimeoutMs + ", " +
                endpoint.HealthCheckExpectedStatusCode + ", " +
                endpoint.HealthyThreshold + ", " +
                endpoint.UnhealthyThreshold + ", " +
                SqliteHelpers.ToSql(endpoint.HealthCheckUseAuth) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.CreatedUtc) + ", " +
                SqliteHelpers.ToSqlRequired(endpoint.LastUpdateUtc) + ");";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return endpoint;
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM model_endpoints WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(string tenantId, EndpointKindEnum? kind, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));

            EnumerationResult<ModelEndpoint> result = new EnumerationResult<ModelEndpoint> { MaxResults = query.MaxResults, Skip = query.Skip };

            string where = " WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId);
            if (kind.HasValue) where += " AND kind = " + SqliteHelpers.ToSqlRequired(kind.Value.ToString());
            if (!String.IsNullOrEmpty(query.SearchTerm)) where += " AND name LIKE '%" + SqliteHelpers.Sanitize(query.SearchTerm) + "%'";

            DataTable countTable = await _Driver.ExecuteQueryAsync("SELECT COUNT(*) AS cnt FROM model_endpoints" + where + ";", false, token).ConfigureAwait(false);
            if (countTable.Rows.Count > 0) result.TotalRecords = SqliteHelpers.GetInt(countTable.Rows[0]["cnt"]);

            StringBuilder sql = new StringBuilder();
            sql.Append("SELECT * FROM model_endpoints").Append(where)
               .Append(" ORDER BY createdutc DESC").Append(_Driver.PaginationClause(query.MaxResults, query.Skip)).Append(";");

            DataTable table = await _Driver.ExecuteQueryAsync(sql.ToString(), false, token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Objects.Add(FromRow(row));

            result.RecordsRemaining = Math.Max(0, result.TotalRecords - query.Skip - result.Objects.Count);
            result.EndOfResults = result.RecordsRemaining == 0;
            if (!result.EndOfResults && result.Objects.Count > 0) result.ContinuationToken = result.Objects[result.Objects.Count - 1].Id;
            return result;
        }

        /// <inheritdoc />
        public async Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            endpoint.LastUpdateUtc = DateTime.UtcNow;

            string query =
                "UPDATE model_endpoints SET " +
                "name = " + SqliteHelpers.ToSqlRequired(endpoint.Name) + ", " +
                "kind = " + SqliteHelpers.ToSqlRequired(endpoint.Kind.ToString()) + ", " +
                "apiformat = " + SqliteHelpers.ToSqlRequired(endpoint.ApiFormat.ToString()) + ", " +
                "hostname = " + SqliteHelpers.ToSqlRequired(endpoint.Hostname) + ", " +
                "port = " + endpoint.Port + ", " +
                "usessl = " + SqliteHelpers.ToSql(endpoint.UseSsl) + ", " +
                "apikey = " + SqliteHelpers.ToSql(endpoint.ApiKey) + ", " +
                "model = " + SqliteHelpers.ToSql(endpoint.Model) + ", " +
                "dimensionality = " + endpoint.Dimensionality + ", " +
                "timeoutms = " + endpoint.TimeoutMs + ", " +
                "active = " + SqliteHelpers.ToSql(endpoint.Active) + ", " +
                "healthcheckurl = " + SqliteHelpers.ToSqlRequired(endpoint.HealthCheckUrl) + ", " +
                "healthcheckmethod = " + SqliteHelpers.ToSqlRequired(endpoint.HealthCheckMethod.ToString()) + ", " +
                "healthcheckintervalms = " + endpoint.HealthCheckIntervalMs + ", " +
                "healthchecktimeoutms = " + endpoint.HealthCheckTimeoutMs + ", " +
                "healthcheckexpectedstatuscode = " + endpoint.HealthCheckExpectedStatusCode + ", " +
                "healthythreshold = " + endpoint.HealthyThreshold + ", " +
                "unhealthythreshold = " + endpoint.UnhealthyThreshold + ", " +
                "healthcheckuseauth = " + SqliteHelpers.ToSql(endpoint.HealthCheckUseAuth) + ", " +
                "lastupdateutc = " + SqliteHelpers.ToSqlRequired(endpoint.LastUpdateUtc) + " " +
                "WHERE tenantid = " + SqliteHelpers.ToSqlRequired(endpoint.TenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(endpoint.Id) + ";";

            await _Driver.ExecuteQueryAsync(query, true, token).ConfigureAwait(false);
            return endpoint;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            ModelEndpoint? existing = await ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (existing == null) return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM model_endpoints WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id = " + SqliteHelpers.ToSqlRequired(id) + ";", true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return new List<ModelEndpoint>();

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM model_endpoints WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", false, token).ConfigureAwait(false);

            List<ModelEndpoint> results = new List<ModelEndpoint>();
            foreach (DataRow row in table.Rows) results.Add(FromRow(row));
            return results;
        }

        /// <inheritdoc />
        public async Task<List<ModelEndpoint>> CreateManyAsync(IReadOnlyCollection<ModelEndpoint> items, CancellationToken token = default)
        {
            if (items == null || items.Count == 0) return new List<ModelEndpoint>();

            List<ModelEndpoint> results = new List<ModelEndpoint>();
            foreach (ModelEndpoint item in items) results.Add(await CreateAsync(item, token).ConfigureAwait(false));
            return results;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default)
        {
            if (ids == null || ids.Count == 0) return 0;

            string inList = String.Join(", ", ids.Select(id => SqliteHelpers.ToSqlRequired(id)));
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM model_endpoints WHERE tenantid = " + SqliteHelpers.ToSqlRequired(tenantId) +
                " AND id IN (" + inList + ");", true, token).ConfigureAwait(false);
            return ids.Count;
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint FromRow(DataRow row)
        {
            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.Id = SqliteHelpers.GetString(row["id"]);
            endpoint.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            endpoint.Name = SqliteHelpers.GetString(row["name"]);
            endpoint.Kind = Enum.TryParse(SqliteHelpers.GetString(row["kind"]), out EndpointKindEnum kind) ? kind : EndpointKindEnum.Embedding;
            endpoint.ApiFormat = Enum.TryParse(SqliteHelpers.GetString(row["apiformat"]), out ApiFormatEnum format) ? format : ApiFormatEnum.OpenAI;
            endpoint.Hostname = SqliteHelpers.GetString(row["hostname"]);
            endpoint.Port = SqliteHelpers.GetInt(row["port"]);
            endpoint.UseSsl = SqliteHelpers.GetBool(row["usessl"]);
            endpoint.ApiKey = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["apikey"]));
            endpoint.Model = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["model"]));
            endpoint.Dimensionality = SqliteHelpers.GetInt(row["dimensionality"]);
            endpoint.TimeoutMs = SqliteHelpers.GetInt(row["timeoutms"], 60000);
            endpoint.Active = SqliteHelpers.GetBool(row["active"]);
            endpoint.HealthCheckUrl = SqliteHelpers.GetString(row["healthcheckurl"]);
            endpoint.HealthCheckMethod = Enum.TryParse(SqliteHelpers.GetString(row["healthcheckmethod"]), out HealthCheckMethodEnum method) ? method : HealthCheckMethodEnum.GET;
            endpoint.HealthCheckIntervalMs = SqliteHelpers.GetInt(row["healthcheckintervalms"], 5000);
            endpoint.HealthCheckTimeoutMs = SqliteHelpers.GetInt(row["healthchecktimeoutms"], 5000);
            endpoint.HealthCheckExpectedStatusCode = SqliteHelpers.GetInt(row["healthcheckexpectedstatuscode"], 200);
            endpoint.HealthyThreshold = SqliteHelpers.GetInt(row["healthythreshold"], 2);
            endpoint.UnhealthyThreshold = SqliteHelpers.GetInt(row["unhealthythreshold"], 2);
            endpoint.HealthCheckUseAuth = SqliteHelpers.GetBool(row["healthcheckuseauth"]);
            endpoint.CreatedUtc = SqliteHelpers.ParseTimestamp(row["createdutc"]);
            endpoint.LastUpdateUtc = SqliteHelpers.ParseTimestamp(row["lastupdateutc"]);
            return endpoint;
        }

        #endregion
    }
}
