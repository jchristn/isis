namespace Isis.Core.Database.Migrations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database.Sqlite;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Migrates model endpoints from the legacy hostname/port/usessl + single apikey shape to a full base URL
    /// plus a typed auth model. No-op on a database already in the new shape.
    /// </summary>
    internal sealed class Migration001ModelEndpointBaseUrlAuth : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-22-model-endpoints-baseurl-auth";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            // Probe for the legacy 'hostname' column. A missing table or column throws; either means there is
            // nothing legacy to migrate (fresh or already-migrated database).
            try
            {
                await driver.ExecuteQueryAsync("SELECT hostname FROM model_endpoints WHERE 1 = 0;", false, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            DataTable table = await driver.ExecuteQueryAsync(
                "SELECT id, tenantid, name, kind, apiformat, hostname, port, usessl, apikey, model, dimensionality, timeoutms, active, " +
                "healthcheckurl, healthcheckmethod, healthcheckintervalms, healthchecktimeoutms, healthcheckexpectedstatuscode, " +
                "healthythreshold, unhealthythreshold, healthcheckuseauth, createdutc, lastupdateutc FROM model_endpoints;", false, token).ConfigureAwait(false);

            List<ModelEndpoint> migrated = new List<ModelEndpoint>();
            foreach (DataRow row in table.Rows) migrated.Add(MapLegacyRow(row));

            await driver.ExecuteQueryAsync("DROP TABLE model_endpoints;", true, token).ConfigureAwait(false);
            await ensureSchema(token).ConfigureAwait(false);

            foreach (ModelEndpoint endpoint in migrated)
            {
                await driver.ModelEndpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint MapLegacyRow(DataRow row)
        {
            string hostname = SqliteHelpers.GetString(row["hostname"]);
            int port = SqliteHelpers.GetInt(row["port"]);
            bool useSsl = SqliteHelpers.GetBool(row["usessl"]);
            string scheme = useSsl ? "https" : "http";
            string baseUrl = scheme + "://" + hostname + (port > 0 ? ":" + port.ToString(CultureInfo.InvariantCulture) : string.Empty);

            ApiFormatEnum apiFormat = Enum.TryParse(SqliteHelpers.GetString(row["apiformat"]), out ApiFormatEnum format) ? format : ApiFormatEnum.OpenAI;
            string? apiKey = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["apikey"]));

            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.Id = SqliteHelpers.GetString(row["id"]);
            endpoint.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            endpoint.Name = SqliteHelpers.GetString(row["name"]);
            endpoint.Kind = Enum.TryParse(SqliteHelpers.GetString(row["kind"]), out EndpointKindEnum kind) ? kind : EndpointKindEnum.Embedding;
            endpoint.ApiFormat = apiFormat;
            endpoint.BaseUrl = baseUrl;

            // Derive the typed auth from the legacy single key: Gemini presented its key in the query string,
            // everything else used a bearer token.
            if (String.IsNullOrEmpty(apiKey))
            {
                endpoint.AuthType = EndpointAuthTypeEnum.None;
            }
            else if (apiFormat == ApiFormatEnum.Gemini)
            {
                endpoint.AuthType = EndpointAuthTypeEnum.QueryParam;
                endpoint.AuthQueryParam = "key";
                endpoint.AuthSecret = apiKey;
            }
            else
            {
                endpoint.AuthType = EndpointAuthTypeEnum.BearerToken;
                endpoint.AuthSecret = apiKey;
            }

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
