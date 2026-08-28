namespace Isis.Server
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Recall;
    using Isis.Core.Stores;
    using Isis.Server.Observability;
    using Isis.Server.Services;
    using Isis.Server.Settings;

    /// <summary>
    /// Composition root. Loads settings, applies environment overrides, constructs the database and server
    /// host, seeds defaults, and blocks until shutdown.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>
        /// Run the server until interrupted.
        /// </summary>
        /// <param name="args">Command-line arguments. The first argument, if present, is the settings file path.</param>
        public static void Run(string[] args)
        {
            string settingsFile = ResolveSettingsFile(args);
            IsisSettings settings = IsisSettings.FromFile(settingsFile);
            settings.ToFile(settingsFile);
            ApplyEnvironmentOverrides(settings);

            Action<string> log = message => Console.WriteLine("[Isis] " + message);
            log("starting node '" + settings.NodeId + "' using settings file '" + settingsFile + "'");

            DatabaseDriverBase database = DatabaseDriverFactory.Create(settings.Database);
            try
            {
                database.InitializeAsync().GetAwaiter().GetResult();
                if (settings.Database.Type == DatabaseTypeEnum.Sqlite)
                {
                    log("database initialized (SQLite at " + settings.Database.Filename + ")");
                }
                else
                {
                    log("database initialized (" + settings.Database.Type + ")");
                }

                DefaultSeeder.SeedAsync(database, settings.Auth, message => log(message)).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                log("database initialization failed: " + e.Message);
                database.Dispose();
                return;
            }

            AuthenticationService authenticationService = new AuthenticationService(database, settings.Auth);
            AuthorizationService authorizationService = new AuthorizationService();

            HttpClient embeddingClient = new HttpClient();
            EmbeddingService embeddingService = new EmbeddingService(embeddingClient);
            StoreOptions storeOptions = new StoreOptions
            {
                RecallDbEndpoint = settings.RecallDb.Endpoint,
                RecallDbAdminKey = settings.RecallDb.AdminApiKey,
                VerbexEndpoint = settings.Verbex.Endpoint
            };
            MemoryService memoryService = new MemoryService(database, embeddingService, storeOptions);

            // Start the observability pipeline before the server so Watson's instrumentation is collected from
            // the first request. A telemetry failure never prevents startup (Start returns null and logs).
            ObservabilityHost? observability = ObservabilityHost.Start(settings.Observability, log);

            IsisServer server = new IsisServer(settings, database, authenticationService, authorizationService, memoryService, log, storeOptions, settingsFile);
            server.Start();
            log("node '" + settings.NodeId + "' listening on " + settings.Rest.Hostname + ":" + settings.Rest.Port);

            ManualResetEventSlim shutdown = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                shutdown.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => shutdown.Set();

            shutdown.Wait();

            log("node '" + settings.NodeId + "' stopping");
            server.Stop();
            server.Dispose();
            observability?.Dispose();
            database.Dispose();
        }

        #endregion

        #region Private-Methods

        private static string ResolveSettingsFile(string[] args)
        {
            string? fromEnv = Environment.GetEnvironmentVariable("ISIS_SETTINGS_FILE");
            if (!String.IsNullOrEmpty(fromEnv)) return fromEnv;
            if (args != null && args.Length > 0 && !String.IsNullOrEmpty(args[0])) return args[0];
            return "isis.json";
        }

        private static void ApplyEnvironmentOverrides(IsisSettings settings)
        {
            string? nodeId = Environment.GetEnvironmentVariable("ISIS_NODE_ID");
            if (!String.IsNullOrEmpty(nodeId)) settings.NodeId = nodeId;

            string? restPort = Environment.GetEnvironmentVariable("ISIS_REST_PORT");
            if (!String.IsNullOrEmpty(restPort) && Int32.TryParse(restPort, out int rp)) settings.Rest.Port = rp;

            string? restHost = Environment.GetEnvironmentVariable("ISIS_REST_HOSTNAME");
            if (!String.IsNullOrEmpty(restHost)) settings.Rest.Hostname = restHost;

            string? recallEndpoint = Environment.GetEnvironmentVariable("ISIS_RECALLDB_ENDPOINT");
            if (!String.IsNullOrEmpty(recallEndpoint)) settings.RecallDb.Endpoint = recallEndpoint;

            string? recallKey = Environment.GetEnvironmentVariable("ISIS_RECALLDB_ADMIN_KEY");
            if (!String.IsNullOrEmpty(recallKey)) settings.RecallDb.AdminApiKey = recallKey;

            string? dbType = Environment.GetEnvironmentVariable("ISIS_DB_TYPE");
            if (!String.IsNullOrEmpty(dbType) && Enum.TryParse<DatabaseTypeEnum>(dbType, true, out DatabaseTypeEnum parsedType)) settings.Database.Type = parsedType;

            string? dbFile = Environment.GetEnvironmentVariable("ISIS_DB_FILENAME");
            if (!String.IsNullOrEmpty(dbFile)) settings.Database.Filename = dbFile;

            string? dbHost = Environment.GetEnvironmentVariable("ISIS_DB_SERVER");
            if (!String.IsNullOrEmpty(dbHost)) settings.Database.Hostname = dbHost;

            string? dbPort = Environment.GetEnvironmentVariable("ISIS_DB_PORT");
            if (!String.IsNullOrEmpty(dbPort) && Int32.TryParse(dbPort, out int dp)) settings.Database.Port = dp;

            string? dbName = Environment.GetEnvironmentVariable("ISIS_DB_DATABASE");
            if (!String.IsNullOrEmpty(dbName)) settings.Database.DatabaseName = dbName;

            string? dbUser = Environment.GetEnvironmentVariable("ISIS_DB_USERNAME");
            if (!String.IsNullOrEmpty(dbUser)) settings.Database.Username = dbUser;

            string? dbPass = Environment.GetEnvironmentVariable("ISIS_DB_PASSWORD");
            if (!String.IsNullOrEmpty(dbPass)) settings.Database.Password = dbPass;

            string? seedAdminEmail = Environment.GetEnvironmentVariable("ISIS_AUTH_SEED_ADMIN_EMAIL");
            if (!String.IsNullOrEmpty(seedAdminEmail)) settings.Auth.SeedAdminEmail = seedAdminEmail;

            string? seedAdminPassword = Environment.GetEnvironmentVariable("ISIS_AUTH_SEED_ADMIN_PASSWORD");
            if (!String.IsNullOrEmpty(seedAdminPassword)) settings.Auth.SeedAdminPassword = seedAdminPassword;

            string? accessKey = Environment.GetEnvironmentVariable("ISIS_AUTH_DEFAULT_ACCESS_KEY");
            if (!String.IsNullOrEmpty(accessKey)) settings.Auth.DefaultAccessKey = accessKey;

            string? secretKey = Environment.GetEnvironmentVariable("ISIS_AUTH_DEFAULT_SECRET_KEY");
            if (!String.IsNullOrEmpty(secretKey)) settings.Auth.DefaultSecretKey = secretKey;

            string? obsEnabled = Environment.GetEnvironmentVariable("ISIS_OBS_ENABLED");
            if (!String.IsNullOrEmpty(obsEnabled) && Boolean.TryParse(obsEnabled, out bool obsEnabledValue)) settings.Observability.Enabled = obsEnabledValue;

            string? otlpEndpoint = Environment.GetEnvironmentVariable("ISIS_OTLP_ENDPOINT");
            if (!String.IsNullOrEmpty(otlpEndpoint)) settings.Observability.OtlpEndpoint = otlpEndpoint;

            string? otlpProtocol = Environment.GetEnvironmentVariable("ISIS_OTLP_PROTOCOL");
            if (!String.IsNullOrEmpty(otlpProtocol)) settings.Observability.OtlpProtocol = otlpProtocol;

            string? obsServiceName = Environment.GetEnvironmentVariable("ISIS_OBS_SERVICE_NAME");
            if (!String.IsNullOrEmpty(obsServiceName)) settings.Observability.ServiceName = obsServiceName;

            string? obsPromHostname = Environment.GetEnvironmentVariable("ISIS_OBS_PROM_HOSTNAME");
            if (!String.IsNullOrEmpty(obsPromHostname)) settings.Observability.PrometheusHostname = obsPromHostname;

            string? obsPromPort = Environment.GetEnvironmentVariable("ISIS_OBS_PROM_PORT");
            if (!String.IsNullOrEmpty(obsPromPort) && Int32.TryParse(obsPromPort, out int obsPromPortValue)) settings.Observability.PrometheusPort = obsPromPortValue;
        }

        #endregion
    }
}
