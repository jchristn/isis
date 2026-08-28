namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Core.Stores;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Memory routes, scoped to a tenant and scope. Includes create/upsert, read, delete, list, and search.
    /// </summary>
    public class MemoryRoutes
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
        /// <param name="memoryService">The memory service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MemoryRoutes(DatabaseDriverBase database, AuthorizationService authorization, MemoryService memoryService)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List memories", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories", UpsertAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create or update a memory", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/search", SearchAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Search memories", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/{memoryId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a memory", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/{memoryId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a memory", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get memories", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/batch", BatchUpsertAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-upsert memories", "Memories"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/memories/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete memories", "Memories"));
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

        private async Task<Scope?> LoadScopeAsync(HttpContextBase context, string tenantId, string scopeId)
        {
            return await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string? categoryId = RouteHelpers.Query(context, "category");
            EnumerationResult<Memory> result = await _Database.Memories.EnumerateAsync(tenantId, scopeId, categoryId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task UpsertAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            Memory? body = RouteHelpers.Body<Memory>(context);
            if (body == null)
            {
                // Distinguish an unparseable-but-present body from a genuinely missing one, so a caller that
                // sent, say, a bad field type is not misdirected to "slug and categoryId".
                if (!string.IsNullOrWhiteSpace(context.Request.DataAsString))
                {
                    await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "The request body could not be parsed as a memory. Check the JSON and field types (note: 'type' is optional and, if set, should be one of User, Feedback, Project, Reference).").ConfigureAwait(false);
                    return;
                }

                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A memory requires a slug and a categoryId.").ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(body.Slug) || string.IsNullOrWhiteSpace(body.CategoryId))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A memory requires a slug and a categoryId.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await LoadScopeAsync(context, tenantId, scopeId).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            Category? category = await _Database.Categories.ReadAsync(tenantId, body.CategoryId, context.Token).ConfigureAwait(false);
            if (category == null || category.ScopeId != scopeId)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "The categoryId does not belong to this scope.").ConfigureAwait(false);
                return;
            }

            try
            {
                Memory saved = await _MemoryService.UpsertAsync(scope, category, body, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                await RouteHelpers.ErrorAsync(context, 501, "NotImplemented", ex.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task SearchAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await LoadScopeAsync(context, tenantId, scopeId).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            MemorySearchQuery query = RouteHelpers.Body<MemorySearchQuery>(context) ?? new MemorySearchQuery();

            try
            {
                MemorySearchResult result = await _MemoryService.SearchAsync(scope, query, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                await RouteHelpers.ErrorAsync(context, 501, "NotImplemented", ex.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string memoryId = RouteHelpers.Param(context, "memoryId") ?? string.Empty;
            Memory? memory = await _Database.Memories.ReadAsync(tenantId, memoryId, context.Token).ConfigureAwait(false);
            if (memory == null || memory.ScopeId != scopeId)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Memory not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, memory).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string memoryId = RouteHelpers.Param(context, "memoryId") ?? string.Empty;
            Memory? memory = await _Database.Memories.ReadAsync(tenantId, memoryId, context.Token).ConfigureAwait(false);
            if (memory == null || memory.ScopeId != scopeId)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Memory not found.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await LoadScopeAsync(context, tenantId, scopeId).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            await _MemoryService.DeleteAsync(scope, memory, context.Token).ConfigureAwait(false);
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
            List<Memory> objects = new List<Memory>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                objects = await _Database.Memories.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchUpsertAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId, out string scopeId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchMemoryRequest? request = RouteHelpers.Body<BatchMemoryRequest>(context);
            List<Memory> objects = new List<Memory>();
            if (request != null && request.Items != null && request.Items.Count > 0)
            {
                Scope? scope = await LoadScopeAsync(context, tenantId, scopeId).ConfigureAwait(false);
                if (scope == null)
                {
                    await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                    return;
                }

                foreach (Memory item in request.Items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Slug) || string.IsNullOrWhiteSpace(item.CategoryId)) continue;

                    Category? category = await _Database.Categories.ReadAsync(tenantId, item.CategoryId, context.Token).ConfigureAwait(false);
                    if (category == null || category.ScopeId != scopeId) continue;

                    Memory saved = await _MemoryService.UpsertAsync(scope, category, item, context.Token).ConfigureAwait(false);
                    objects.Add(saved);
                }
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 201, body).ConfigureAwait(false);
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
                Scope? scope = await LoadScopeAsync(context, tenantId, scopeId).ConfigureAwait(false);
                if (scope == null)
                {
                    await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                    return;
                }

                foreach (string memoryId in request.Ids)
                {
                    Memory? memory = await _Database.Memories.ReadAsync(tenantId, memoryId, context.Token).ConfigureAwait(false);
                    if (memory == null || memory.ScopeId != scopeId) continue;

                    await _MemoryService.DeleteAsync(scope, memory, context.Token).ConfigureAwait(false);
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
