namespace Isis.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Isis.Core.Security;
    using Isis.Core.Stores;
    using Isis.Core.Stores.RecallDb;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using RecallDb.Sdk;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// RecallDB collection management, proxied through to RecallDB's REST API (Isis does not re-implement
    /// collection storage). Available only when a RecallDB endpoint is configured.
    /// </summary>
    public class CollectionRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authorization;
        private readonly StoreOptions? _StoreOptions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="authorization">The authorization service.</param>
        /// <param name="storeOptions">Store options carrying the RecallDB endpoint and key.</param>
        /// <exception cref="ArgumentNullException">Thrown when authorization is null.</exception>
        public CollectionRoutes(AuthorizationService authorization, StoreOptions? storeOptions)
        {
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _StoreOptions = storeOptions;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/collections", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List RecallDB collections", "Collections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/collections", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a RecallDB collection", "Collections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/collections/{collectionId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a RecallDB collection", "Collections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/collections/{collectionId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a RecallDB collection", "Collections"));
        }

        #endregion

        #region Private-Methods

        private RecallDbCollectionProxy? BuildProxy()
        {
            if (_StoreOptions == null || string.IsNullOrEmpty(_StoreOptions.RecallDbEndpoint) || string.IsNullOrEmpty(_StoreOptions.RecallDbAdminKey)) return null;
            return new RecallDbCollectionProxy(_StoreOptions.RecallDbEndpoint!, _StoreOptions.RecallDbAdminKey!);
        }

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

            RecallDbCollectionProxy? proxy = BuildProxy();
            if (proxy == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "RecallDbNotConfigured", "No RecallDB endpoint is configured. Collection management requires RecallDB.").ConfigureAwait(false);
                return;
            }

            try
            {
                int maxResults = RouteHelpers.QueryInt(context, "maxResults") ?? 100;
                await RouteHelpers.JsonAsync(context, 200, await proxy.ListAsync(tenantId, maxResults, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (RecallDbException ex)
            {
                await RouteHelpers.ErrorAsync(context, 502, "RecallDbError", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            RecallDbCollectionProxy? proxy = BuildProxy();
            if (proxy == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "RecallDbNotConfigured", "No RecallDB endpoint is configured. Collection management requires RecallDB.").ConfigureAwait(false);
                return;
            }

            CollectionCreateRequest? request = RouteHelpers.Body<CollectionCreateRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Dimensionality < 1)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A collection name and a positive dimensionality are required.").ConfigureAwait(false);
                return;
            }

            try
            {
                await RouteHelpers.JsonAsync(context, 201, await proxy.CreateAsync(tenantId, request.Name, request.Dimensionality, request.Description, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (RecallDbException ex)
            {
                await RouteHelpers.ErrorAsync(context, 502, "RecallDbError", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            RecallDbCollectionProxy? proxy = BuildProxy();
            if (proxy == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "RecallDbNotConfigured", "No RecallDB endpoint is configured. Collection management requires RecallDB.").ConfigureAwait(false);
                return;
            }

            string collectionId = RouteHelpers.Param(context, "collectionId") ?? string.Empty;
            try
            {
                await RouteHelpers.JsonAsync(context, 200, await proxy.ReadAsync(tenantId, collectionId, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (RecallDbException ex)
            {
                await RouteHelpers.ErrorAsync(context, 502, "RecallDbError", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            RecallDbCollectionProxy? proxy = BuildProxy();
            if (proxy == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "RecallDbNotConfigured", "No RecallDB endpoint is configured. Collection management requires RecallDB.").ConfigureAwait(false);
                return;
            }

            string collectionId = RouteHelpers.Param(context, "collectionId") ?? string.Empty;
            try
            {
                await proxy.DeleteAsync(tenantId, collectionId, context.Token).ConfigureAwait(false);
                context.Response.StatusCode = 204;
                await context.Response.Send().ConfigureAwait(false);
            }
            catch (RecallDbException ex)
            {
                await RouteHelpers.ErrorAsync(context, 502, "RecallDbError", ex.Message).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
