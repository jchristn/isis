namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
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
        private readonly MemoryService _MemoryService;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <param name="memoryService">The memory service (used for cascade delete of scope content).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ScopeRoutes(DatabaseDriverBase database, AuthorizationService authorization, MemoryService memoryService)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List scopes", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a scope", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get scopes", "Scopes"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete scopes", "Scopes"));
        }

        #endregion

        #region Private-Methods

        private bool Authorize(HttpContextBase context, out RequestContext ctx, out string tenantId)
        {
            ctx = RouteHelpers.Context(context);
            tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            return _Authorization.CanAccessTenant(ctx, tenantId);
        }

        private async Task<ModelEndpoint?> FirstActiveEmbeddingEndpointAsync(string tenantId, System.Threading.CancellationToken token)
        {
            EnumerationResult<ModelEndpoint> result = await _Database.ModelEndpoints.EnumerateAsync(tenantId, EndpointKindEnum.Embedding, new EnumerationQuery { MaxResults = 50 }, token).ConfigureAwait(false);
            foreach (ModelEndpoint endpoint in result.Objects)
            {
                if (endpoint.Active) return endpoint;
            }

            return null;
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

            // Resolve store configuration so a minimally-specified scope is actually usable. The default store
            // is RecallDb, which needs an embedding endpoint and a matching dimensionality; auto-wire the
            // tenant's embedding endpoint (and adopt its dimensionality) when the caller did not specify one,
            // and return an actionable error — rather than persist a silently broken scope — when RecallDb is
            // requested but no embedding endpoint exists.
            if (scope.StoreProvider == StoreProviderEnum.RecallDb)
            {
                ModelEndpoint? endpoint;
                if (!string.IsNullOrEmpty(scope.EmbeddingEndpointId))
                {
                    endpoint = await _Database.ModelEndpoints.ReadAsync(tenantId, scope.EmbeddingEndpointId, context.Token).ConfigureAwait(false);
                    if (endpoint == null)
                    {
                        await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "The specified embeddingEndpointId was not found in this tenant.").ConfigureAwait(false);
                        return;
                    }
                }
                else
                {
                    endpoint = await FirstActiveEmbeddingEndpointAsync(tenantId, context.Token).ConfigureAwait(false);
                }

                if (endpoint == null)
                {
                    await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A RecallDb scope needs an embedding endpoint, but none is configured for this tenant. Create the scope with storeProvider 'Filesystem' or 'Verbex' for keyword-only memory, or configure an embedding endpoint first (list them with isis_endpoint_enumerate).").ConfigureAwait(false);
                    return;
                }

                scope.EmbeddingEndpointId = endpoint.Id;
                if (scope.Dimensionality <= 0) scope.Dimensionality = endpoint.Dimensionality;
                if (scope.Dimensionality <= 0)
                {
                    await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "The embedding endpoint '" + endpoint.Id + "' has no dimensionality configured; pass 'dimensionality' explicitly (e.g. 384 for all-minilm).").ConfigureAwait(false);
                    return;
                }
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
            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            // Cascade: tear down store content and delete all categories + memories, then the scope.
            await _MemoryService.DeleteScopeAsync(scope, context.Token).ConfigureAwait(false);

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task BatchGetAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            List<Scope> objects = new List<Scope>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                objects = await _Database.Scopes.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchDeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            int deleted = 0;
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                foreach (string scopeId in request.Ids)
                {
                    Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
                    if (scope == null) continue;

                    // Cascade: tear down store content and delete all categories + memories, then the scope.
                    await _MemoryService.DeleteScopeAsync(scope, context.Token).ConfigureAwait(false);
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
