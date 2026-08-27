namespace Isis.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Scope routes, scoped to a tenant.
    /// </summary>
    public class ScopeRoutes
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
        public ScopeRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List scopes", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a scope", "Scopes"));
        }

        #endregion

        #region Private-Methods

        private bool Authorize(HttpContextBase context, out RequestContext ctx, out string tenantId)
        {
            ctx = RouteHelpers.Context(context);
            tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            return _Authorization.CanAccessTenant(ctx, tenantId);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<Scope> result = await _Database.Scopes.EnumerateAsync(tenantId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            Scope? scope = RouteHelpers.Body<Scope>(context);
            if (scope == null || string.IsNullOrWhiteSpace(scope.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A scope name is required.").ConfigureAwait(false);
                return;
            }

            scope.TenantId = tenantId;
            Scope? conflict = await _Database.Scopes.ReadByNameAsync(tenantId, scope.Name, context.Token).ConfigureAwait(false);
            if (conflict != null)
            {
                await RouteHelpers.ErrorAsync(context, 409, "Conflict", "A scope with that name already exists.").ConfigureAwait(false);
                return;
            }

            Scope created = await _Database.Scopes.CreateAsync(scope, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;
            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, scope).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;
            Scope? existing = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            Scope? update = RouteHelpers.Body<Scope>(context);
            if (update == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A scope body is required.").ConfigureAwait(false);
                return;
            }

            update.Id = scopeId;
            update.TenantId = tenantId;
            update.CreatedUtc = existing.CreatedUtc;
            update.StoreProvider = existing.StoreProvider;
            update.Dimensionality = existing.Dimensionality;
            update.RecallCollectionId = existing.RecallCollectionId;
            Scope saved = await _Database.Scopes.UpdateAsync(update, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;
            bool deleted = await _Database.Scopes.DeleteAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
