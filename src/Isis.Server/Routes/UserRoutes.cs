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
    /// User administration routes, scoped to a tenant. Managing users is an administrative operation gated by
    /// IsAdmin or IsTenantAdmin. Passwords are hashed server-side and never returned.
    /// </summary>
    public class UserRoutes
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
        public UserRoutes(DatabaseDriverBase database, AuthorizationService authorization)
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/users", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List users", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/users", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/users/{userId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/users/{userId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/users/{userId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete a user", "Users"));
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
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage users for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<User> result = await _Database.Users.EnumerateAsync(tenantId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            List<Dictionary<string, object?>> items = new List<Dictionary<string, object?>>();
            foreach (User user in result.Objects) items.Add(View(user));

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
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage users for this tenant.").ConfigureAwait(false);
                return;
            }

            UserUpsertRequest? request = RouteHelpers.Body<UserUpsertRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "email and password are required.").ConfigureAwait(false);
                return;
            }

            User? conflict = await _Database.Users.ReadByEmailAsync(tenantId, request.Email.Trim(), context.Token).ConfigureAwait(false);
            if (conflict != null)
            {
                await RouteHelpers.ErrorAsync(context, 409, "Conflict", "A user with that email already exists in this tenant.").ConfigureAwait(false);
                return;
            }

            User user = new User();
            user.TenantId = tenantId;
            user.Email = request.Email.Trim();
            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.PasswordSha256 = PasswordHasher.Hash(request.Password);
            user.IsAdmin = request.IsAdmin;
            user.IsTenantAdmin = request.IsTenantAdmin;
            user.Active = request.Active;

            User created = await _Database.Users.CreateAsync(user, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, View(created)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage users for this tenant.").ConfigureAwait(false);
                return;
            }

            string userId = RouteHelpers.Param(context, "userId") ?? string.Empty;
            User? user = await _Database.Users.ReadAsync(tenantId, userId, context.Token).ConfigureAwait(false);
            if (user == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, View(user)).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage users for this tenant.").ConfigureAwait(false);
                return;
            }

            string userId = RouteHelpers.Param(context, "userId") ?? string.Empty;
            User? existing = await _Database.Users.ReadAsync(tenantId, userId, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }

            UserUpsertRequest? request = RouteHelpers.Body<UserUpsertRequest>(context);
            if (request == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A user body is required.").ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(request.Email)) existing.Email = request.Email.Trim();
            existing.FirstName = request.FirstName;
            existing.LastName = request.LastName;
            existing.IsAdmin = request.IsAdmin;
            existing.IsTenantAdmin = request.IsTenantAdmin;
            existing.Active = request.Active;
            if (!string.IsNullOrEmpty(request.Password)) existing.PasswordSha256 = PasswordHasher.Hash(request.Password);

            User saved = await _Database.Users.UpdateAsync(existing, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, View(saved)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            if (!Authorize(context, out _, out string tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage users for this tenant.").ConfigureAwait(false);
                return;
            }

            string userId = RouteHelpers.Param(context, "userId") ?? string.Empty;

            // Cascade: remove credentials owned by this user within the tenant.
            EnumerationResult<Credential> owned = await _Database.Credentials.EnumerateAsync(tenantId, new EnumerationQuery { MaxResults = 1000 }, context.Token).ConfigureAwait(false);
            foreach (Credential credential in owned.Objects)
            {
                if (string.Equals(credential.UserId, userId, StringComparison.Ordinal))
                {
                    await _Database.Credentials.DeleteAsync(tenantId, credential.Id, context.Token).ConfigureAwait(false);
                }
            }

            bool deleted = await _Database.Users.DeleteAsync(tenantId, userId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private static Dictionary<string, object?> View(User user)
        {
            Dictionary<string, object?> view = new Dictionary<string, object?>();
            view["id"] = user.Id;
            view["tenantId"] = user.TenantId;
            view["firstName"] = user.FirstName;
            view["lastName"] = user.LastName;
            view["email"] = user.Email;
            view["isAdmin"] = user.IsAdmin;
            view["isTenantAdmin"] = user.IsTenantAdmin;
            view["active"] = user.Active;
            view["protected"] = user.Protected;
            view["createdUtc"] = user.CreatedUtc;
            view["lastUpdateUtc"] = user.LastUpdateUtc;
            return view;
        }

        #endregion
    }
}
