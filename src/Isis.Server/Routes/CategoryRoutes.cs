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
    /// Category routes, scoped to a tenant and scope.
    /// </summary>
    public class CategoryRoutes
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
        public CategoryRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List categories", "Categories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a category", "Categories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories/{categoryId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a category", "Categories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories/{categoryId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a category", "Categories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories/{categoryId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a category", "Categories"));
        }

        #endregion

        #region Private-Methods

        private bool Authorize(HttpContextBase context, out string tenantId, out string scopeId)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;
            return _Authorization.CanAccessTenant(ctx, tenantId);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<Category> result = await _Database.Categories.EnumerateAsync(tenantId, scopeId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            Category? category = RouteHelpers.Body<Category>(context);
            if (category == null || string.IsNullOrWhiteSpace(category.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A category name is required.").ConfigureAwait(false);
                return;
            }

            category.TenantId = tenantId;
            category.ScopeId = scopeId;
            Category? conflict = await _Database.Categories.ReadByNameAsync(tenantId, scopeId, category.Name, context.Token).ConfigureAwait(false);
            if (conflict != null)
            {
                await RouteHelpers.ErrorAsync(context, 409, "Conflict", "A category with that name already exists in this scope.").ConfigureAwait(false);
                return;
            }

            Category created = await _Database.Categories.CreateAsync(category, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string categoryId = RouteHelpers.Param(context, "categoryId") ?? string.Empty;
            Category? category = await _Database.Categories.ReadAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
            if (category == null || category.ScopeId != scopeId)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Category not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, category).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string categoryId = RouteHelpers.Param(context, "categoryId") ?? string.Empty;
            Category? existing = await _Database.Categories.ReadAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
            if (existing == null || existing.ScopeId != scopeId)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Category not found.").ConfigureAwait(false);
                return;
            }

            Category? update = RouteHelpers.Body<Category>(context);
            if (update == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A category body is required.").ConfigureAwait(false);
                return;
            }

            update.Id = categoryId;
            update.TenantId = tenantId;
            update.ScopeId = scopeId;
            update.CreatedUtc = existing.CreatedUtc;
            Category saved = await _Database.Categories.UpdateAsync(update, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string categoryId = RouteHelpers.Param(context, "categoryId") ?? string.Empty;
            bool deleted = await _Database.Categories.DeleteAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Category not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
