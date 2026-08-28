namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Health;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Model endpoint routes (embedding and inference), scoped to a tenant, including a live health probe.
    /// </summary>
    public class ModelEndpointRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthorizationService _Authorization;
        private readonly HealthCheckService _HealthCheck;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <param name="healthCheck">The health-check service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ModelEndpointRoutes(DatabaseDriverBase database, AuthorizationService authorization, HealthCheckService healthCheck)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _HealthCheck = healthCheck ?? throw new ArgumentNullException(nameof(healthCheck));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/endpoints", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List model endpoints", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/endpoints", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a model endpoint", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/endpoints/{endpointId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a model endpoint", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/endpoints/{endpointId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a model endpoint", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/endpoints/{endpointId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a model endpoint", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/endpoint-health", HealthAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Probe model endpoint health", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/endpoints/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get model endpoints", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/endpoints/batch", BatchCreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-create model endpoints", "Endpoints"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/endpoints/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete model endpoints", "Endpoints"));
        }

        #endregion

        #region Private-Methods

        private bool Authorize(HttpContextBase context, out string tenantId)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            return _Authorization.CanAccessTenant(ctx, tenantId);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            EndpointKindEnum? kind = null;
            string? kindText = RouteHelpers.Query(context, "kind");
            if (!string.IsNullOrEmpty(kindText) && Enum.TryParse(kindText, true, out EndpointKindEnum parsed)) kind = parsed;

            EnumerationResult<ModelEndpoint> result = await _Database.ModelEndpoints.EnumerateAsync(tenantId, kind, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            ModelEndpoint? endpoint = RouteHelpers.Body<ModelEndpoint>(context);
            if (endpoint == null || string.IsNullOrWhiteSpace(endpoint.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "An endpoint name is required.").ConfigureAwait(false);
                return;
            }

            endpoint.TenantId = tenantId;
            endpoint.Id = IdGenerator.Endpoint(endpoint.Kind);
            ModelEndpoint created = await _Database.ModelEndpoints.CreateAsync(endpoint, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string endpointId = RouteHelpers.Param(context, "endpointId") ?? string.Empty;
            ModelEndpoint? endpoint = await _Database.ModelEndpoints.ReadAsync(tenantId, endpointId, context.Token).ConfigureAwait(false);
            if (endpoint == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Endpoint not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, endpoint).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string endpointId = RouteHelpers.Param(context, "endpointId") ?? string.Empty;
            ModelEndpoint? existing = await _Database.ModelEndpoints.ReadAsync(tenantId, endpointId, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Endpoint not found.").ConfigureAwait(false);
                return;
            }

            ModelEndpoint? update = RouteHelpers.Body<ModelEndpoint>(context);
            if (update == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "An endpoint body is required.").ConfigureAwait(false);
                return;
            }

            update.Id = endpointId;
            update.TenantId = tenantId;
            update.CreatedUtc = existing.CreatedUtc;
            ModelEndpoint saved = await _Database.ModelEndpoints.UpdateAsync(update, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string endpointId = RouteHelpers.Param(context, "endpointId") ?? string.Empty;
            bool deleted = await _Database.ModelEndpoints.DeleteAsync(tenantId, endpointId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Endpoint not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task HealthAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<ModelEndpoint> endpoints = await _Database.ModelEndpoints.EnumerateAsync(tenantId, null, new EnumerationQuery { MaxResults = 1000 }, context.Token).ConfigureAwait(false);
            int probes = await _HealthCheck.ProbeOnceAsync(endpoints.Objects, context.Token).ConfigureAwait(false);

            List<Dictionary<string, object?>> statuses = new List<Dictionary<string, object?>>();
            foreach (ModelEndpoint endpoint in endpoints.Objects)
            {
                EndpointHealthStatus? status = _HealthCheck.GetStatus(endpoint.Id);
                Dictionary<string, object?> entry = new Dictionary<string, object?>();
                entry["id"] = endpoint.Id;
                entry["name"] = endpoint.Name;
                entry["kind"] = endpoint.Kind.ToString();
                entry["baseUrl"] = endpoint.GetBaseUrl();
                entry["status"] = status;
                statuses.Add(entry);
            }

            Dictionary<string, object?> response = new Dictionary<string, object?>();
            response["probesPerformed"] = probes;
            response["endpointCount"] = endpoints.Objects.Count;
            response["endpoints"] = statuses;
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task BatchGetAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            List<ModelEndpoint> objects = new List<ModelEndpoint>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                objects = await _Database.ModelEndpoints.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchCreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchModelEndpointRequest? request = RouteHelpers.Body<BatchModelEndpointRequest>(context);
            List<ModelEndpoint> objects = new List<ModelEndpoint>();
            if (request != null && request.Items != null && request.Items.Count > 0)
            {
                foreach (ModelEndpoint item in request.Items)
                {
                    item.TenantId = tenantId;
                    item.Id = IdGenerator.Endpoint(item.Kind);
                }

                objects = await _Database.ModelEndpoints.CreateManyAsync(request.Items, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 201, body).ConfigureAwait(false);
        }

        private async Task BatchDeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            int deleted = 0;
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                deleted = await _Database.ModelEndpoints.DeleteManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["deleted"] = deleted;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        #endregion
    }
}
