namespace Isis.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// Resolves inbound requests to a typed <see cref="RequestContext"/>. Supports two schemes: a session
    /// bearer token issued by email/password login (Authorization: Bearer or x-token) and a per-tenant
    /// credential identified by its access key (x-access-key). The access key is the public, transferable
    /// material and authenticates on its own; a secret key (x-secret-key) is optional and, when presented, must
    /// match. Admin power derives solely from the resolved user's IsAdmin / IsTenantAdmin flags.
    /// </summary>
    public class AuthenticationService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthSettings _AuthSettings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the authentication service.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authSettings">The authentication settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public AuthenticationService(DatabaseDriverBase database, AuthSettings authSettings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _AuthSettings = authSettings ?? throw new ArgumentNullException(nameof(authSettings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Watson authentication hook for post-authentication routes. On success attaches a
        /// <see cref="RequestContext"/> to the context metadata; on failure sends a 401 and stops routing.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Awaitable task.</returns>
        public async Task AuthenticateRequestAsync(HttpContextBase context)
        {
            RequestContext resolved = await ResolveAsync(context, context.Token).ConfigureAwait(false);
            if (!resolved.IsAuthenticated)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.Send("{\"error\":\"Unauthorized\",\"message\":\"Authentication required or invalid.\"}").ConfigureAwait(false);
                return;
            }

            context.Metadata = resolved;
        }

        /// <summary>
        /// Resolve a request to a request context without sending a response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved request context.</returns>
        public async Task<RequestContext> ResolveAsync(HttpContextBase context, CancellationToken token = default)
        {
            string? sessionToken = ReadSessionToken(context);
            if (!string.IsNullOrEmpty(sessionToken))
            {
                return await BuildSessionContextAsync(sessionToken, token).ConfigureAwait(false);
            }

            string? accessKey = context.Request.Headers["x-access-key"];
            if (!string.IsNullOrEmpty(accessKey))
            {
                Credential? credential = await _Database.Credentials.ReadByAccessKeyAsync(accessKey, token).ConfigureAwait(false);
                if (credential != null && credential.Active)
                {
                    if (credential.ExpirationUtc.HasValue && credential.ExpirationUtc.Value < DateTime.UtcNow) return RequestContext.Unauthenticated();

                    // The access key is the public, transferable material and is sufficient on its own — this
                    // is what lets single-header MCP clients (e.g. Mux, via 'Authorization: Bearer <accessKey>')
                    // authenticate without ever transmitting the secret. When a client DOES present a secret it
                    // must match; a mismatched secret is rejected outright rather than silently ignored.
                    string? secretKey = context.Request.Headers["x-secret-key"];
                    if (!string.IsNullOrEmpty(secretKey))
                    {
                        if (string.IsNullOrEmpty(credential.SecretKey) || !PasswordHasher.FixedTimeEquals(secretKey, credential.SecretKey)) return RequestContext.Unauthenticated();
                    }

                    return await BuildCredentialContextAsync(credential, token).ConfigureAwait(false);
                }
            }

            return RequestContext.Unauthenticated();
        }

        #endregion

        #region Private-Methods

        private static string? ReadSessionToken(HttpContextBase context)
        {
            string? bearer = null;
            string? authorization = context.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                bearer = authorization.Substring("Bearer ".Length).Trim();
            }

            string? xToken = context.Request.Headers["x-token"];
            if (!string.IsNullOrEmpty(bearer) && !string.IsNullOrEmpty(xToken) && !PasswordHasher.FixedTimeEquals(bearer, xToken)) return null;
            return !string.IsNullOrEmpty(bearer) ? bearer : xToken;
        }

        private async Task<RequestContext> BuildSessionContextAsync(string sessionToken, CancellationToken token)
        {
            AuthSession? session = await _Database.Sessions.ReadByTokenAsync(sessionToken, token).ConfigureAwait(false);
            if (session == null || !session.Active) return RequestContext.Unauthenticated();
            if (session.RevokedUtc.HasValue) return RequestContext.Unauthenticated();
            if (session.ExpirationUtc < DateTime.UtcNow) return RequestContext.Unauthenticated();
            if (string.IsNullOrEmpty(session.UserId)) return RequestContext.Unauthenticated();

            User? user = await _Database.Users.ReadAsync(session.TenantId, session.UserId, token).ConfigureAwait(false);
            if (user == null || !user.Active) return RequestContext.Unauthenticated();

            RequestContext context = new RequestContext();
            context.IsAuthenticated = true;
            context.PrincipalType = PrincipalTypeEnum.User;
            context.TenantId = session.TenantId;
            context.UserId = user.Id;
            context.SessionId = session.Id;
            context.IsAdmin = user.IsAdmin;
            context.IsTenantAdmin = user.IsTenantAdmin;
            context.PrincipalName = user.Email;
            context.User = user;
            return context;
        }

        private async Task<RequestContext> BuildCredentialContextAsync(Credential credential, CancellationToken token)
        {
            RequestContext context = new RequestContext();
            context.IsAuthenticated = true;
            context.PrincipalType = PrincipalTypeEnum.Credential;
            context.TenantId = credential.TenantId;
            context.CredentialId = credential.Id;
            context.PrincipalName = credential.Name;
            context.Credential = credential;

            if (!string.IsNullOrEmpty(credential.UserId))
            {
                User? owner = await _Database.Users.ReadAsync(credential.TenantId, credential.UserId, token).ConfigureAwait(false);
                if (owner != null && owner.Active)
                {
                    context.UserId = owner.Id;
                    context.IsAdmin = owner.IsAdmin;
                    context.IsTenantAdmin = owner.IsTenantAdmin;
                }
            }

            return context;
        }

        #endregion
    }
}
