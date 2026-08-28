namespace Isis.Server
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.RegularExpressions;
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
        private readonly HttpClient _InferenceClient;
        private readonly HealthCheckService _HealthCheck;
        private readonly InferenceService _InferenceService;
        private readonly MemoryChatService _ChatService;
        private readonly Webserver _Server;
        private readonly Action<string>? _Log;
        private readonly StoreOptions? _StoreOptions;
        private readonly string? _SettingsFile;
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
        /// <param name="settingsFile">Optional settings file path, enabling the server settings routes to persist changes.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public IsisServer(
            IsisSettings settings,
            DatabaseDriverBase database,
            AuthenticationService authenticationService,
            AuthorizationService authorizationService,
            MemoryService memoryService,
            Action<string>? log = null,
            StoreOptions? storeOptions = null,
            string? settingsFile = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _AuthenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _AuthorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _MemoryService = memoryService ?? throw new ArgumentNullException(nameof(memoryService));
            _Log = log;
            _StoreOptions = storeOptions;
            _SettingsFile = settingsFile;

            _ProbeClient = new HttpClient();
            _HealthCheck = new HealthCheckService(_ProbeClient);
            // Inference streams can run far longer than the default 100s HttpClient timeout; rely on the
            // per-request cancellation token rather than a hard client timeout.
            _InferenceClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _InferenceService = new InferenceService(_InferenceClient);
            _ChatService = new MemoryChatService(_MemoryService, _InferenceService);

            WebserverSettings webserverSettings = new WebserverSettings();
            webserverSettings.Hostname = settings.Rest.Hostname;
            webserverSettings.Port = settings.Rest.Port;
            webserverSettings.Ssl.Enable = settings.Rest.Ssl;

            // Enable Watson's built-in OpenTelemetry instrumentation (metrics + traces). The signals are
            // emitted into Watson's default meter/activity source ("Watson"); the in-process ObservabilityHost
            // subscribes to them by name and exports metrics via Prometheus and traces via OTLP. Watson's own
            // Prometheus endpoint stays off — the OTel MeterProvider owns the scrape endpoint on port 9464.
            webserverSettings.Telemetry.Enable = true;
            webserverSettings.Telemetry.EnableMetrics = true;
            webserverSettings.Telemetry.EnableTraces = true;
            webserverSettings.Telemetry.PropagateContext = true;
            webserverSettings.Telemetry.Prometheus.Enable = false;

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
            TenantLifecycleService tenantLifecycle = new TenantLifecycleService(_Database, _MemoryService);
            new AuthRoutes(_Database, Settings.Auth).Register(_Server);
            new TenantRoutes(_Database, _AuthorizationService, tenantLifecycle).Register(_Server);
            new UserRoutes(_Database, _AuthorizationService).Register(_Server);
            new CredentialRoutes(_Database, _AuthorizationService).Register(_Server);
            new ScopeRoutes(_Database, _AuthorizationService, _MemoryService).Register(_Server);
            new CategoryRoutes(_Database, _AuthorizationService, _MemoryService).Register(_Server);
            new MemoryRoutes(_Database, _AuthorizationService, _MemoryService).Register(_Server);
            new ModelEndpointRoutes(_Database, _AuthorizationService, _HealthCheck).Register(_Server);
            new ChatRoutes(_Database, _AuthorizationService, _ChatService).Register(_Server);
            new RequestHistoryRoutes(_Database, _AuthorizationService).Register(_Server);
            new CollectionRoutes(_AuthorizationService, _StoreOptions).Register(_Server);
            new GuideRoutes(_Database, _AuthorizationService).Register(_Server);
            new InstructionRoutes(_Database, _AuthorizationService).Register(_Server);
            new SettingsRoutes(Settings, _SettingsFile ?? "isis.json", _AuthorizationService).Register(_Server);
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
            if (path.Contains("/api/health", StringComparison.Ordinal) || path.Contains("/metrics", StringComparison.Ordinal))
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

                if (Settings.RequestHistory.CaptureHeaders)
                {
                    entry.RequestHeaders = BuildHeadersJson(context.Request.Headers);
                    entry.ResponseHeaders = BuildHeadersJson(context.Response.Headers);
                }

                if (Settings.RequestHistory.CaptureBodies)
                {
                    int maxBytes = Settings.RequestHistory.MaxBodyBytes > 0 ? Settings.RequestHistory.MaxBodyBytes : 16384;
                    entry.RequestBody = CaptureBody(SafeRequestBody(context), maxBytes);
                    entry.ResponseBody = CaptureBody(RouteHelpers.TakeCapturedResponseBody(context), maxBytes);
                }

                await _Database.RequestHistory.CreateAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort capture; never fail the request because of history.
            }
        }

        private static string? SafeRequestBody(HttpContextBase context)
        {
            try
            {
                return context.Request.DataAsString;
            }
            catch
            {
                return null;
            }
        }

        private static string? BuildHeadersJson(NameValueCollection? headers)
        {
            if (headers == null) return null;

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? key in headers.AllKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                map[key] = IsSensitiveHeader(key) ? "***" : (headers[key] ?? string.Empty);
            }

            if (map.Count == 0) return null;
            return JsonSerializer.Serialize(map);
        }

        private static bool IsSensitiveHeader(string key)
        {
            string lower = key.ToLowerInvariant();
            return lower == "authorization" || lower == "x-secret-key" || lower == "x-token" || lower == "cookie" || lower == "set-cookie";
        }

        private static string? CaptureBody(string? body, int maxBytes)
        {
            if (string.IsNullOrEmpty(body)) return null;

            string redacted = Regex.Replace(
                body,
                "(\"(?:password|secretKey|secret_key|secret)\"\\s*:\\s*)\"[^\"]*\"",
                "$1\"***\"",
                RegexOptions.IgnoreCase);

            if (redacted.Length > maxBytes) redacted = redacted.Substring(0, maxBytes) + "…[truncated]";
            return redacted;
        }

        private void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                if (_Server is IDisposable disposableServer) disposableServer.Dispose();
                _ProbeClient.Dispose();
                _InferenceClient.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
