namespace Isis.McpServer
{
    using System;
    using System.Threading;
    using Isis.McpServer.Settings;

    /// <summary>
    /// Composition root for the MCP server. Loads settings, applies environment overrides, starts the MCP
    /// transport, and blocks until shutdown.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>
        /// Run the MCP server until interrupted.
        /// </summary>
        /// <param name="args">Command-line arguments. The first argument, if present, is the settings file path.</param>
        public static void Run(string[] args)
        {
            string settingsFile = ResolveSettingsFile(args);
            McpServerSettings settings = McpServerSettings.FromFile(settingsFile);
            settings.ToFile(settingsFile);
            ApplyEnvironmentOverrides(settings);

            Console.WriteLine("[Isis.Mcp] starting, proxying REST at " + settings.RestBaseUrl());

            using CancellationTokenSource cts = new CancellationTokenSource();
            IsisMcpServer server = new IsisMcpServer(settings);
            server.Start(cts.Token);
            Console.WriteLine("[Isis.Mcp] MCP server listening on http://" + settings.Hostname + ":" + settings.Port + settings.McpPath + " (streamable HTTP + SSE)");

            ManualResetEventSlim shutdown = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                shutdown.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => shutdown.Set();

            shutdown.Wait();

            Console.WriteLine("[Isis.Mcp] stopping");
            server.Stop();
            server.Dispose();
            cts.Cancel();
        }

        #endregion

        #region Private-Methods

        private static string ResolveSettingsFile(string[] args)
        {
            string? fromEnv = Environment.GetEnvironmentVariable("ISIS_MCP_SETTINGS_FILE");
            if (!String.IsNullOrEmpty(fromEnv)) return fromEnv;
            if (args != null && args.Length > 0 && !String.IsNullOrEmpty(args[0])) return args[0];
            return "isis.mcp.json";
        }

        private static void ApplyEnvironmentOverrides(McpServerSettings settings)
        {
            string? port = Environment.GetEnvironmentVariable("ISIS_MCP_PORT");
            if (!String.IsNullOrEmpty(port) && Int32.TryParse(port, out int p)) settings.Port = p;

            string? host = Environment.GetEnvironmentVariable("ISIS_MCP_HOSTNAME");
            if (!String.IsNullOrEmpty(host)) settings.Hostname = host;

            string? restHost = Environment.GetEnvironmentVariable("ISIS_MCP_REST_HOSTNAME");
            if (!String.IsNullOrEmpty(restHost)) settings.RestHostname = restHost;

            string? restPort = Environment.GetEnvironmentVariable("ISIS_MCP_REST_PORT");
            if (!String.IsNullOrEmpty(restPort) && Int32.TryParse(restPort, out int rp)) settings.RestPort = rp;
        }

        #endregion
    }
}
