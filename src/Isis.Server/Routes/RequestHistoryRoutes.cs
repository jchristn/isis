namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Request history routes. System administrators see all traffic; tenant principals see only their own.
    /// </summary>
    public class RequestHistoryRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthorizationService _Authorization;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RequestHistoryRoutes(DatabaseDriverBase database, AuthorizationService authorization)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/requests", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List request history", "RequestHistory"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, "/v1.0/api/requests", ClearAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Clear request history", "RequestHistory"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/requests/{id}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a request history entry", "RequestHistory"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!ctx.IsAuthenticated)
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Authentication required.").ConfigureAwait(false);
                return;
            }

            string? tenantFilter = ctx.IsAdmin ? null : ctx.TenantId;
            if (!ctx.IsAdmin && string.IsNullOrEmpty(tenantFilter))
            {
                await RouteHelpers.JsonAsync(context, 200, new EnumerationResult<RequestHistoryEntry>()).ConfigureAwait(false);
                return;
            }

            EnumerationResult<RequestHistoryEntry> result = await _Database.RequestHistory.EnumerateAsync(tenantFilter, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string id = RouteHelpers.Param(context, "id") ?? string.Empty;
            RequestHistoryEntry? entry = await _Database.RequestHistory.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (entry == null || (!ctx.IsAdmin && !string.Equals(entry.TenantId, ctx.TenantId, StringComparison.Ordinal)))
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Request history entry not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, entry).ConfigureAwait(false);
        }

        private async Task ClearAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string? tenantFilter;

            if (ctx.IsAdmin)
            {
                tenantFilter = null;
            }
            else if (ctx.IsTenantAdmin && !string.IsNullOrEmpty(ctx.TenantId))
            {
                tenantFilter = ctx.TenantId;
            }
            else
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Only administrators may clear request history.").ConfigureAwait(false);
                return;
            }

            long deleted = await _Database.RequestHistory.DeleteAllAsync(tenantFilter, context.Token).ConfigureAwait(false);
            Dictionary<string, object?> response = new Dictionary<string, object?> { ["deleted"] = deleted };
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
