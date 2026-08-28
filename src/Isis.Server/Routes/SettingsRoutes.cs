namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Security;
    using Isis.Server.Services;
    using Isis.Server.Settings;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Server settings routes. System administrators can read and modify the effective server settings, and
    /// trigger a node restart. Changes are written to the settings file; fields read per-request (request
    /// history) are applied immediately, while listener/database/integration changes require a restart.
    /// </summary>
    public class SettingsRoutes
    {
        #region Private-Members

        private readonly IsisSettings _Settings;
        private readonly string _SettingsFile;
        private readonly AuthorizationService _Authorization;

        // Sections whose values are read per-request and therefore take effect without a restart.
        private static readonly string[] _LiveSections = new[] { "requestHistory" };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">The running server settings instance.</param>
        /// <param name="settingsFile">The settings file path to persist to.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SettingsRoutes(IsisSettings settings, string settingsFile, AuthorizationService authorization)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _SettingsFile = string.IsNullOrEmpty(settingsFile) ? "isis.json" : settingsFile;
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
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/settings", GetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read server settings", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/v1.0/api/settings", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update server settings", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/api/settings/restart", RestartAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Restart the server node", "Settings"));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!ctx.IsAdmin)
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Server settings require a system administrator.").ConfigureAwait(false);
                return;
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["settings"] = _Settings;
            body["settingsFile"] = _SettingsFile;
            body["liveSections"] = _LiveSections;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!ctx.IsAdmin)
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Server settings require a system administrator.").ConfigureAwait(false);
                return;
            }

            IsisSettings? incoming = RouteHelpers.Body<IsisSettings>(context);
            if (incoming == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A settings body is required.").ConfigureAwait(false);
                return;
            }

            // Persist the full settings to the file so the next boot picks them up.
            try
            {
                incoming.ToFile(_SettingsFile);
            }
            catch (Exception e)
            {
                await RouteHelpers.ErrorAsync(context, 500, "WriteFailed", "Could not write the settings file: " + e.Message).ConfigureAwait(false);
                return;
            }

            // Apply the per-request-read fields to the running instance so they take effect immediately.
            _Settings.RequestHistory.Enabled = incoming.RequestHistory.Enabled;

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["settings"] = incoming;
            body["settingsFile"] = _SettingsFile;
            body["liveSections"] = _LiveSections;
            body["restartRequired"] = true;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task RestartAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            if (!ctx.IsAdmin)
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Restarting the node requires a system administrator.").ConfigureAwait(false);
                return;
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["restarting"] = true;
            body["message"] = "Node is restarting; Docker will relaunch it with the current settings.";
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);

            // Exit shortly after responding; the container restart policy relaunches the node.
            _ = Task.Run(async () =>
            {
                await Task.Delay(500).ConfigureAwait(false);
                Environment.Exit(0);
            });
        }

        #endregion
    }
}
