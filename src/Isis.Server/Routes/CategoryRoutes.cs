namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
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
        private readonly MemoryService _MemoryService;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <param name="memoryService">The memory service (used for cascade delete of category memories).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public CategoryRoutes(DatabaseDriverBase database, AuthorizationService authorization, MemoryService memoryService)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _MemoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get categories", "Categories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/categories/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete categories", "Categories"));
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
            Category? category = await _Database.Categories.ReadAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
            if (category == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Category not found.").ConfigureAwait(false);
                return;
            }

            // Cascade: delete the category's memories (store + index), then the category row.
            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope != null) await _MemoryService.DeleteCategoryMemoriesAsync(scope, categoryId, context.Token).ConfigureAwait(false);
            await _Database.Categories.DeleteAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task BatchGetAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            List<Category> objects = new List<Category>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                objects = await _Database.Categories.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchDeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            int deleted = 0;
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                foreach (string categoryId in request.Ids)
                {
                    Category? category = await _Database.Categories.ReadAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
                    if (category == null || category.ScopeId != scopeId) continue;

                    // Cascade: delete the category's memories (store + index), then the category row.
                    Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
                    if (scope != null) await _MemoryService.DeleteCategoryMemoriesAsync(scope, categoryId, context.Token).ConfigureAwait(false);
                    await _Database.Categories.DeleteAsync(tenantId, categoryId, context.Token).ConfigureAwait(false);
                    deleted++;
                }
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["deleted"] = deleted;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        #endregion
    }
}
