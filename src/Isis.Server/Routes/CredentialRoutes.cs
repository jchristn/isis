namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Credential administration routes, scoped to a tenant. Managing credentials is an administrative
    /// operation gated by IsAdmin or IsTenantAdmin. The server generates the access key and secret key; the
    /// raw secret is returned only once, at creation.
    /// </summary>
    public class CredentialRoutes
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
        public CredentialRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credentials", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List credentials", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credentials", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/credentials/{credentialId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/credentials/{credentialId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/credentials/{credentialId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a credential", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credentials/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get credentials", "Credentials"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/credentials/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete credentials", "Credentials"));
        }

        #endregion

        #region Private-Methods

        private bool Authorize(HttpContextBase context, out RequestContext ctx, out string tenantId)
        {
            ctx = RouteHelpers.Context(context);
            tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            return _Authorization.CanAdministerTenant(ctx, tenantId);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<Credential> result = await _Database.Credentials.EnumerateAsync(tenantId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            List<Dictionary<string, object?>> items = new List<Dictionary<string, object?>>();
            foreach (Credential credential in result.Objects) items.Add(View(credential));

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = items;
            body["totalRecords"] = result.TotalRecords;
            body["recordsRemaining"] = result.RecordsRemaining;
            body["maxResults"] = result.MaxResults;
            body["skip"] = result.Skip;
            body["endOfResults"] = result.EndOfResults;
            body["continuationToken"] = result.ContinuationToken;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            CredentialUpsertRequest? request = RouteHelpers.Body<CredentialUpsertRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.UserId))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "userId is required.").ConfigureAwait(false);
                return;
            }

            User? owner = await _Database.Users.ReadAsync(tenantId, request.UserId.Trim(), context.Token).ConfigureAwait(false);
            if (owner == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "The owning user does not exist in this tenant.").ConfigureAwait(false);
                return;
            }

            string secret = "secret_" + IdGenerator.Token();
            Credential credential = new Credential();
            credential.TenantId = tenantId;
            credential.UserId = owner.Id;
            credential.Name = request.Name;
            credential.AccessKey = "access_" + IdGenerator.Token();
            credential.SecretKey = secret;
            credential.Active = request.Active;
            credential.ExpirationUtc = request.ExpirationUtc;

            Credential created = await _Database.Credentials.CreateAsync(credential, context.Token).ConfigureAwait(false);

            // Return the raw secret exactly once.
            Dictionary<string, object?> view = View(created);
            view["secretKey"] = secret;
            await RouteHelpers.JsonAsync(context, 201, view).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            string credentialId = RouteHelpers.Param(context, "credentialId") ?? string.Empty;
            Credential? credential = await _Database.Credentials.ReadAsync(tenantId, credentialId, context.Token).ConfigureAwait(false);
            if (credential == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Credential not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, View(credential)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            string credentialId = RouteHelpers.Param(context, "credentialId") ?? string.Empty;
            Credential? existing = await _Database.Credentials.ReadAsync(tenantId, credentialId, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Credential not found.").ConfigureAwait(false);
                return;
            }

            CredentialUpsertRequest? request = RouteHelpers.Body<CredentialUpsertRequest>(context);
            if (request == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A credential body is required.").ConfigureAwait(false);
                return;
            }

            existing.Name = request.Name;
            existing.Active = request.Active;
            existing.ExpirationUtc = request.ExpirationUtc;

            Credential saved = await _Database.Credentials.UpdateAsync(existing, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, View(saved)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            string credentialId = RouteHelpers.Param(context, "credentialId") ?? string.Empty;
            bool deleted = await _Database.Credentials.DeleteAsync(tenantId, credentialId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Credential not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task BatchGetAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            List<Dictionary<string, object?>> objects = new List<Dictionary<string, object?>>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                List<Credential> credentials = await _Database.Credentials.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
                foreach (Credential credential in credentials) objects.Add(View(credential));
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchDeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage credentials for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            int deleted = 0;
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                deleted = await _Database.Credentials.DeleteManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["deleted"] = deleted;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private static Dictionary<string, object?> View(Credential credential)
        {
            string? last4 = null;
            if (!string.IsNullOrEmpty(credential.SecretKey) && credential.SecretKey.Length >= 4)
            {
                last4 = credential.SecretKey.Substring(credential.SecretKey.Length - 4);
            }

            Dictionary<string, object?> view = new Dictionary<string, object?>();
            view["id"] = credential.Id;
            view["tenantId"] = credential.TenantId;
            view["userId"] = credential.UserId;
            view["name"] = credential.Name;
            view["accessKey"] = credential.AccessKey;
            view["secretKeyLast4"] = last4;
            view["authMode"] = credential.AuthMode.ToString();
            view["active"] = credential.Active;
            view["protected"] = credential.Protected;
            view["createdUtc"] = credential.CreatedUtc;
            view["lastUpdateUtc"] = credential.LastUpdateUtc;
            view["lastUsedUtc"] = credential.LastUsedUtc;
            view["expirationUtc"] = credential.ExpirationUtc;
            return view;
        }

        #endregion
    }
}
