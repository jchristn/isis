namespace Isis.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Anonymous health endpoint reporting node identity and database connectivity.
    /// </summary>
    public class HealthRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly string _NodeId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="nodeId">The node identifier.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public HealthRoutes(DatabaseDriverBase database, string nodeId)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _NodeId = string.IsNullOrEmpty(nodeId) ? "isis" : nodeId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(
                HttpMethod.GET, "/v1.0/api/health", HealthAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Server health check", "System"));
        }

        #endregion

        #region Private-Methods

        private async Task HealthAsync(HttpContextBase context)
        {
            bool healthy;
            try
            {
                healthy = await _Database.PingAsync(context.Token).ConfigureAwait(false);
            }
            catch
            {
                healthy = false;
            }

            context.Response.StatusCode = healthy ? 200 : 503;
            context.Response.ContentType = "application/json";
            string body =
                "{\"status\":\"" + (healthy ? "healthy" : "degraded") + "\"," +
                "\"node\":\"" + _NodeId + "\"," +
                "\"database\":" + (healthy ? "true" : "false") + "," +
                "\"utc\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
            await context.Response.Send(body).ConfigureAwait(false);
        }

        #endregion
    }
}
