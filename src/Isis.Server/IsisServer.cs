namespace Isis.Server
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Health;
    using Isis.Core.Models;
    using Isis.Core.Recall;
    using Isis.Core.Security;
    using Isis.Core.Stores;
    using Isis.Server.Routes;
    using Isis.Server.Services;
    using Isis.Server.Settings;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// The Isis REST server host. Owns the Watson webserver and wires the authentication hook, OpenAPI,
    /// CORS, and the feature route registrars.
    /// </summary>
    public class IsisServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Effective server settings.
        /// </summary>
        public IsisSettings Settings { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthenticationService _AuthenticationService;
        private readonly AuthorizationService _AuthorizationService;
        private readonly MemoryService _MemoryService;
        private readonly HttpClient _ProbeClient;
        private readonly HealthCheckService _HealthCheck;
        private readonly InferenceService _InferenceService;
        private readonly MemoryChatService _ChatService;
        private readonly Webserver _Server;
        private readonly Action<string>? _Log;
        private readonly StoreOptions? _StoreOptions;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the server host.
        /// </summary>
        /// <param name="settings">Server settings.</param>
        /// <param name="database">Initialized database driver.</param>
        /// <param name="authenticationService">Authentication service.</param>
        /// <param name="authorizationService">Authorization service.</param>
        /// <param name="memoryService">Memory service.</param>
        /// <param name="log">Optional log callback.</param>
        /// <param name="storeOptions">Optional external store options (RecallDB/Verbex).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public IsisServer(
            IsisSettings settings,
            DatabaseDriverBase database,
            AuthenticationService authenticationService,
            AuthorizationService authorizationService,
            MemoryService memoryService,
            Action<string>? log = null,
            StoreOptions? storeOptions = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _AuthenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _AuthorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _MemoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
            _Log = log;
            _StoreOptions = storeOptions;

            _ProbeClient = new HttpClient();
            _HealthCheck = new HealthCheckService(_ProbeClient);
            _InferenceService = new InferenceService(_ProbeClient);
            _ChatService = new MemoryChatService(_MemoryService, _InferenceService);

            WebserverSettings webserverSettings = new WebserverSettings();
            webserverSettings.Hostname = settings.Rest.Hostname;
            webserverSettings.Port = settings.Rest.Port;
            webserverSettings.Ssl.Enable = settings.Rest.Ssl;

            _Server = new Webserver(webserverSettings, DefaultRouteAsync);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Configure the pipeline and routes, then start listening.
        /// </summary>
        public void Start()
        {
            ConfigureServer();
            ConfigureRoutes();
            _Server.Start();
        }

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Stop()
        {
            if (_Server.IsListening) _Server.Stop();
        }

        /// <summary>
        /// Dispose the server host.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private void ConfigureServer()
        {
            _Server.Routes.AuthenticateRequest = _AuthenticationService.AuthenticateRequestAsync;
            _Server.Routes.Preflight = PreflightRouteAsync;
            _Server.Routes.PostRouting = PostRoutingRouteAsync;

            _Server.UseOpenApi(openApi =>
            {
                openApi.Info.Title = "Isis API";
                openApi.Info.Version = "v1.0";
                openApi.Info.Description = "Isis agent memory platform.";
            });
        }

        private void ConfigureRoutes()
        {
            new HealthRoutes(_Database, Settings.NodeId).Register(_Server);
            new ServerInfoRoutes(Settings.NodeId).Register(_Server);
            new AuthRoutes(_Database, Settings.Auth).Register(_Server);
            new TenantRoutes(_Database, _AuthorizationService).Register(_Server);
            new UserRoutes(_Database, _AuthorizationService).Register(_Server);
            new CredentialRoutes(_Database, _AuthorizationService).Register(_Server);
            new ScopeRoutes(_Database, _AuthorizationService).Register(_Server);
            new CategoryRoutes(_Database, _AuthorizationService).Register(_Server);
            new MemoryRoutes(_Database, _AuthorizationService, _MemoryService).Register(_Server);
            new ModelEndpointRoutes(_Database, _AuthorizationService, _HealthCheck).Register(_Server);
            new ChatRoutes(_Database, _AuthorizationService, _ChatService).Register(_Server);
            new RequestHistoryRoutes(_Database, _AuthorizationService).Register(_Server);
            new CollectionRoutes(_AuthorizationService, _StoreOptions).Register(_Server);
            new GuideRoutes(_Database, _AuthorizationService).Register(_Server);
        }

        private static async Task DefaultRouteAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            await context.Response.Send("{\"error\":\"NotFound\",\"message\":\"No matching route.\"}").ConfigureAwait(false);
        }

        private static async Task PreflightRouteAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 200;
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, HEAD");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-access-key, x-secret-key, x-token");
            context.Response.Headers.Add("Access-Control-Max-Age", "86400");
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task PostRoutingRouteAsync(HttpContextBase context)
        {
            context.Timestamp.End = DateTime.UtcNow;
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, HEAD");
            context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-access-key, x-secret-key, x-token");

            if (_Log != null)
            {
                _Log(context.Request.Method + " " + context.Request.Url.RawWithQuery + " " + context.Response.StatusCode);
            }

            await CaptureRequestAsync(context).ConfigureAwait(false);
        }

        private async Task CaptureRequestAsync(HttpContextBase context)
        {
            if (!Settings.RequestHistory.Enabled)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            string path = context.Request.Url.RawWithoutQuery ?? string.Empty;
            if (path.Contains("/api/health", StringComparison.Ordinal))
            {
                await Task.CompletedTask.ConfigureAwait(false);
                return;
            }

            try
            {
                RequestContext? ctx = context.Metadata as RequestContext;
                RequestHistoryEntry entry = new RequestHistoryEntry();
                entry.Method = context.Request.Method.ToString();
                entry.Path = context.Request.Url.RawWithQuery ?? path;
                entry.StatusCode = context.Response.StatusCode;
                entry.DurationMs = context.Timestamp.TotalMs ?? 0.0;
                entry.TenantId = ctx?.TenantId;
                entry.PrincipalName = ctx?.PrincipalName;
                entry.SourceIp = context.Request.Source?.IpAddress;
                await _Database.RequestHistory.CreateAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort capture; never fail the request because of history.
            }
        }

        private void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                if (_Server is IDisposable disposableServer) disposableServer.Dispose();
                _ProbeClient.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
