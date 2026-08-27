namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Core.Stores;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// The guide route returns the operating manual for a scope: its categories and their usage
    /// instructions, plus the retrieval capabilities of its store. An agent should call this first.
    /// </summary>
    public class GuideRoutes
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
        public GuideRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Parameter.Add(
                HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/guide", GuideAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Get the scope memory guide", "Guide"));
        }

        #endregion

        #region Private-Methods

        private async Task GuideAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;

            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            EnumerationQuery query = new EnumerationQuery { MaxResults = 1000 };
            EnumerationResult<Category> categories = await _Database.Categories.EnumerateAsync(tenantId, scopeId, query, context.Token).ConfigureAwait(false);
            StoreCapabilities capabilities = MemoryStoreFactory.Create(scope).Capabilities;

            List<Dictionary<string, object?>> categoryList = new List<Dictionary<string, object?>>();
            foreach (Category category in categories.Objects)
            {
                Dictionary<string, object?> entry = new Dictionary<string, object?>();
                entry["id"] = category.Id;
                entry["name"] = category.Name;
                entry["description"] = category.Description;
                entry["instructions"] = category.Instructions;
                categoryList.Add(entry);
            }

            Dictionary<string, object?> response = new Dictionary<string, object?>();
            response["instructions"] = "Call this guide first. Write one memory per idea. Use create/upsert with a stable slug so re-writing updates in place. Choose the category whose instructions match what you are recording.";
            response["scope"] = new Dictionary<string, object?>
            {
                ["id"] = scope.Id,
                ["name"] = scope.Name,
                ["description"] = scope.Description,
                ["storeProvider"] = scope.StoreProvider.ToString(),
                ["dimensionality"] = scope.Dimensionality
            };
            response["capabilities"] = capabilities;
            response["categories"] = categoryList;

            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
