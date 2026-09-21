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
    /// Operation event routes. System administrators see all activity; tenant principals see only their own.
    /// Managed consistently with request history (same enumeration, clear, and background retention).
    /// </summary>
    public class OperationRoutes
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
        public OperationRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/operations", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List operation events", "Operations"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, "/v1.0/api/operations", ClearAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Clear operation events", "Operations"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/operations/{id}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read an operation event", "Operations"));
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
                await RouteHelpers.JsonAsync(context, 200, new EnumerationResult<OperationEvent>()).ConfigureAwait(false);
                return;
            }

            string? resourceType = RouteHelpers.Query(context, "resourceType");
            string? operation = RouteHelpers.Query(context, "operation");
            EnumerationResult<OperationEvent> result = await _Database.OperationEvents.EnumerateAsync(tenantFilter, resourceType, operation, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string id = RouteHelpers.Param(context, "id") ?? string.Empty;
            OperationEvent? entry = await _Database.OperationEvents.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (entry == null || (!ctx.IsAdmin && !string.Equals(entry.TenantId, ctx.TenantId, StringComparison.Ordinal)))
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Operation event not found.").ConfigureAwait(false);
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
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Only administrators may clear operation events.").ConfigureAwait(false);
                return;
            }

            long deleted = await _Database.OperationEvents.DeleteAllAsync(tenantFilter, context.Token).ConfigureAwait(false);
            Dictionary<string, object?> response = new Dictionary<string, object?> { ["deleted"] = deleted };
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
