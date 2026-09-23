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
    /// Adds the optional maxinputtokens override to model endpoints (used for chunk-budget sizing). Runs after
    /// the base-URL/auth migration, so it reads the already-migrated endpoint shape. No-op when the column is
    /// already present.
    /// </summary>
    internal sealed class Migration004EndpointMaxInputTokens : ISchemaMigration
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => "2026-09-23-endpoint-max-input-tokens";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ApplyAsync(DatabaseDriverBase driver, Func<CancellationToken, Task> ensureSchema, CancellationToken token)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));

            try
            {
                await driver.ExecuteQueryAsync("SELECT maxinputtokens FROM model_endpoints WHERE 1 = 0;", false, token).ConfigureAwait(false);
                return;
            }
            catch
            {
                // Column (or table) absent — handled below.
            }

            DataTable table;
            try
            {
                table = await driver.ExecuteQueryAsync(
                    "SELECT id, tenantid, name, kind, apiformat, baseurl, authtype, authheadername, authsecretheadername, authqueryparam, authkeyid, authsecret, " +
                    "model, dimensionality, timeoutms, active, healthcheckurl, healthcheckmethod, healthcheckintervalms, healthchecktimeoutms, " +
                    "healthcheckexpectedstatuscode, healthythreshold, unhealthythreshold, healthcheckuseauth, createdutc, lastupdateutc FROM model_endpoints;", false, token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            List<ModelEndpoint> migrated = new List<ModelEndpoint>();
            foreach (DataRow row in table.Rows) migrated.Add(MapRow(row));

            await driver.ExecuteQueryAsync("DROP TABLE model_endpoints;", true, token).ConfigureAwait(false);
            await ensureSchema(token).ConfigureAwait(false);

            foreach (ModelEndpoint endpoint in migrated)
            {
                await driver.ModelEndpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint MapRow(DataRow row)
        {
            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.Id = SqliteHelpers.GetString(row["id"]);
            endpoint.TenantId = SqliteHelpers.GetString(row["tenantid"]);
            endpoint.Name = SqliteHelpers.GetString(row["name"]);
            endpoint.Kind = Enum.TryParse(SqliteHelpers.GetString(row["kind"]), out EndpointKindEnum kind) ? kind : EndpointKindEnum.Embedding;
            endpoint.ApiFormat = Enum.TryParse(SqliteHelpers.GetString(row["apiformat"]), out ApiFormatEnum format) ? format : ApiFormatEnum.OpenAI;
            endpoint.MaxInputTokens = 0; // new override defaults to auto
            endpoint.BaseUrl = SqliteHelpers.GetString(row["baseurl"]);
            endpoint.AuthType = Enum.TryParse(SqliteHelpers.GetString(row["authtype"]), out EndpointAuthTypeEnum authType) ? authType : EndpointAuthTypeEnum.None;
            endpoint.AuthHeaderName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["authheadername"]));
            endpoint.AuthSecretHeaderName = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["authsecretheadername"]));
            endpoint.AuthQueryParam = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["authqueryparam"]));
            endpoint.AuthKeyId = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["authkeyid"]));
            endpoint.AuthSecret = SqliteHelpers.NullIfEmpty(SqliteHelpers.GetString(row["authsecret"]));
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
