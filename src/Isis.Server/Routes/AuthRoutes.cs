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
    using Isis.Server.Settings;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Authentication routes: pre-auth tenant discovery and email/password token issuance, plus authenticated
    /// whoami and logout. Admin power derives from the resolved user's IsAdmin / IsTenantAdmin flags.
    /// </summary>
    public class AuthRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthSettings _Auth;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="auth">Authentication settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AuthRoutes(DatabaseDriverBase database, AuthSettings auth)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Auth = auth ?? throw new ArgumentNullException(nameof(auth));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PreAuthentication.Static.Add(
                HttpMethod.POST, "/v1.0/api/tenants-for-email", TenantsForEmailAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("List the tenants an email address belongs to", "Authentication"));
            server.Routes.PreAuthentication.Static.Add(
                HttpMethod.POST, "/v1.0/api/token", LoginAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Issue a session token from email and password", "Authentication"));

            server.Routes.PostAuthentication.Static.Add(
                HttpMethod.GET, "/v1.0/api/whoami", WhoAmIAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Resolve the current principal", "Authentication"));
            server.Routes.PostAuthentication.Static.Add(
                HttpMethod.DELETE, "/v1.0/api/token", LogoutAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Revoke the current session token (logout)", "Authentication"));
        }

        #endregion

        #region Private-Methods

        private async Task TenantsForEmailAsync(HttpContextBase context)
        {
            TenantsForEmailRequest? request = RouteHelpers.Body<TenantsForEmailRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "An email address is required.").ConfigureAwait(false);
                return;
            }

            List<User> users = await _Database.Users.EnumerateByEmailAsync(request.Email.Trim(), context.Token).ConfigureAwait(false);
            List<Dictionary<string, object?>> tenants = new List<Dictionary<string, object?>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (User user in users)
            {
                if (!user.Active) continue;
                if (seen.Contains(user.TenantId)) continue;

                Tenant? tenant = await _Database.Tenants.ReadAsync(user.TenantId, context.Token).ConfigureAwait(false);
                if (tenant == null || !tenant.Active) continue;

                seen.Add(user.TenantId);
                Dictionary<string, object?> entry = new Dictionary<string, object?>();
                entry["id"] = tenant.Id;
                entry["name"] = tenant.Name;
                tenants.Add(entry);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["tenants"] = tenants;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task LoginAsync(HttpContextBase context)
        {
            LoginRequest? request = RouteHelpers.Body<LoginRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) || string.IsNullOrWhiteSpace(request.TenantId))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "email, password, and tenantId are required.").ConfigureAwait(false);
                return;
            }

            Tenant? tenant = await _Database.Tenants.ReadAsync(request.TenantId.Trim(), context.Token).ConfigureAwait(false);
            if (tenant == null || !tenant.Active)
            {
                await RouteHelpers.ErrorAsync(context, 401, "Unauthorized", "Invalid credentials.").ConfigureAwait(false);
                return;
            }

            User? user = await _Database.Users.ReadByEmailAsync(request.TenantId.Trim(), request.Email.Trim(), context.Token).ConfigureAwait(false);
            if (user == null || !user.Active || !PasswordHasher.Verify(request.Password, user.PasswordSha256))
            {
                await RouteHelpers.ErrorAsync(context, 401, "Unauthorized", "Invalid credentials.").ConfigureAwait(false);
                return;
            }

            AuthSession session = new AuthSession();
            session.TenantId = user.TenantId;
            session.UserId = user.Id;
            session.PrincipalType = PrincipalTypeEnum.User;
            session.AuthScheme = AuthSchemeEnum.PasswordHeaders;
            session.IssuedUtc = DateTime.UtcNow;
            session.ExpirationUtc = DateTime.UtcNow.AddMinutes(_Auth.SessionLifetimeMinutes);
            session.SourceIp = context.Request.Source?.IpAddress;
            session.UserAgent = context.Request.Headers["User-Agent"];
            AuthSession created = await _Database.Sessions.CreateAsync(session, context.Token).ConfigureAwait(false);

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["token"] = created.Token;
            body["tenantId"] = created.TenantId;
            body["userId"] = created.UserId;
            body["email"] = user.Email;
            body["isAdmin"] = user.IsAdmin;
            body["isTenantAdmin"] = user.IsTenantAdmin;
            body["expiresUtc"] = created.ExpirationUtc;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task LogoutAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!string.IsNullOrEmpty(ctx.SessionId))
            {
                AuthSession? session = await _Database.Sessions.ReadAsync(ctx.SessionId, context.Token).ConfigureAwait(false);
                if (session != null && session.Active)
                {
                    session.Active = false;
                    session.RevokedUtc = DateTime.UtcNow;
                    session.RevocationReason = "logout";
                    await _Database.Sessions.UpdateAsync(session, context.Token).ConfigureAwait(false);
                }
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task WhoAmIAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            Dictionary<string, object?> who = new Dictionary<string, object?>();
            who["isAuthenticated"] = ctx.IsAuthenticated;
            who["principalType"] = ctx.PrincipalType?.ToString();
            who["principalName"] = ctx.PrincipalName;
            who["tenantId"] = ctx.TenantId;
            who["userId"] = ctx.UserId;
            who["credentialId"] = ctx.CredentialId;
            who["isAdmin"] = ctx.IsAdmin;
            who["isTenantAdmin"] = ctx.IsTenantAdmin;
            await RouteHelpers.JsonAsync(context, 200, who).ConfigureAwait(false);
        }

        #endregion
    }
}
