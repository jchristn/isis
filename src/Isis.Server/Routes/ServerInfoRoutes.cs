namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Anonymous server information endpoint.
    /// </summary>
    public class ServerInfoRoutes
    {
        #region Private-Members

        private readonly string _NodeId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        public ServerInfoRoutes(string nodeId)
        {
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
                HttpMethod.GET, "/v1.0/api/server/info", InfoAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Server information", "System"));
        }

        #endregion

        #region Private-Methods

        private async Task InfoAsync(HttpContextBase context)
        {
            Dictionary<string, object> info = new Dictionary<string, object>();
            info["product"] = Constants.ProductName;
            info["version"] = Constants.ProductVersion;
            info["node"] = _NodeId;
            info["utc"] = DateTime.UtcNow.ToString("o");
            await RouteHelpers.JsonAsync(context, 200, info).ConfigureAwait(false);
        }

        #endregion
    }
}
