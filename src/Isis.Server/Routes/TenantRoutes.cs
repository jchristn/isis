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
    /// Tenant administration routes.
    /// </summary>
    public class TenantRoutes
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
        public TenantRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/tenants", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List tenants", "Tenants"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/tenants", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{id}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{id}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a tenant", "Tenants"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{id}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a tenant", "Tenants"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            EnumerationQuery query = RouteHelpers.Enumeration(context);

            if (_Authorization.CanManageTenants(ctx))
            {
                EnumerationResult<Tenant> result = await _Database.Tenants.EnumerateAsync(query, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
                return;
            }

            EnumerationResult<Tenant> scoped = new EnumerationResult<Tenant> { MaxResults = query.MaxResults };
            if (!string.IsNullOrEmpty(ctx.TenantId))
            {
                Tenant? own = await _Database.Tenants.ReadAsync(ctx.TenantId, context.Token).ConfigureAwait(false);
                if (own != null) scoped.Objects.Add(own);
            }

            scoped.TotalRecords = scoped.Objects.Count;
            await RouteHelpers.JsonAsync(context, 200, scoped).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!_Authorization.CanManageTenants(ctx))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Only a system administrator may create tenants.").ConfigureAwait(false);
                return;
            }

            Tenant? tenant = RouteHelpers.Body<Tenant>(context);
            if (tenant == null || string.IsNullOrWhiteSpace(tenant.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A tenant name is required.").ConfigureAwait(false);
                return;
            }

            Tenant created = await _Database.Tenants.CreateAsync(tenant, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string id = RouteHelpers.Param(context, "id") ?? string.Empty;
            if (!_Authorization.CanAccessTenant(ctx, id))
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }

            Tenant? tenant = await _Database.Tenants.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (tenant == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, tenant).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string id = RouteHelpers.Param(context, "id") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, id))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to update this tenant.").ConfigureAwait(false);
                return;
            }

            Tenant? existing = await _Database.Tenants.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }

            Tenant? update = RouteHelpers.Body<Tenant>(context);
            if (update == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A tenant body is required.").ConfigureAwait(false);
                return;
            }

            update.Id = id;
            update.CreatedUtc = existing.CreatedUtc;
            Tenant saved = await _Database.Tenants.UpdateAsync(update, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string id = RouteHelpers.Param(context, "id") ?? string.Empty;
            if (!_Authorization.CanManageTenants(ctx))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Only a system administrator may delete tenants.").ConfigureAwait(false);
                return;
            }

            bool deleted = await _Database.Tenants.DeleteAsync(id, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Tenant not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
