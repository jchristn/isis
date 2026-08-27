namespace Isis.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Isis.McpServer.Settings;

    /// <summary>
    /// Automates connecting an agent client (Claude Code, or a project <c>.mcp.json</c>) to the Isis MCP
    /// server by upserting an "isis" entry into the client's configuration. Other servers and unknown keys
    /// are preserved, and the target file is backed up before writing.
    /// </summary>
    public static class McpInstaller
    {
        #region Public-Methods

        /// <summary>
        /// Run the installer with the given command-line arguments (those following the "install" verb).
        /// </summary>
        /// <param name="args">The install arguments.</param>
        /// <returns>A process exit code.</returns>
        public static int Run(string[] args)
        {
            string settingsFile = Environment.GetEnvironmentVariable("ISIS_MCP_SETTINGS_FILE") ?? "isis.mcp.json";
            McpServerSettings settings = McpServerSettings.FromFile(settingsFile);

            string host = settings.Hostname == "*" || string.IsNullOrEmpty(settings.Hostname) ? "127.0.0.1" : settings.Hostname;
            int port = settings.Port;
            string accessKey = "isisdefaultkey";
            string secretKey = "isisdefaultsecret";
            bool project = false;
            string? explicitUrl = null;

            string? envHost = Environment.GetEnvironmentVariable("ISIS_MCP_HOSTNAME");
            if (!string.IsNullOrEmpty(envHost) && envHost != "*") host = envHost;
            string? envPort = Environment.GetEnvironmentVariable("ISIS_MCP_PORT");
            if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out int ep)) port = ep;
            string? envAccessKey = Environment.GetEnvironmentVariable("ISIS_MCP_ACCESS_KEY") ?? Environment.GetEnvironmentVariable("ISIS_AUTH_DEFAULT_ACCESS_KEY");
            if (!string.IsNullOrEmpty(envAccessKey)) accessKey = envAccessKey;
            string? envSecretKey = Environment.GetEnvironmentVariable("ISIS_MCP_SECRET_KEY") ?? Environment.GetEnvironmentVariable("ISIS_AUTH_DEFAULT_SECRET_KEY");
            if (!string.IsNullOrEmpty(envSecretKey)) secretKey = envSecretKey;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--access-key":
                        if (i + 1 < args.Length) accessKey = args[++i];
                        break;
                    case "--secret-key":
                        if (i + 1 < args.Length) secretKey = args[++i];
                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) { port = p; i++; }
                        break;
                    case "--host":
                        if (i + 1 < args.Length) host = args[++i];
                        break;
                    case "--url":
                        if (i + 1 < args.Length) explicitUrl = args[++i];
                        break;
                    case "--project":
                        project = true;
                        break;
                }
            }

            string url = explicitUrl ?? ("http://" + host + ":" + port + settings.McpPath);
            string target = ResolveTarget(project);

            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["x-access-key"] = accessKey,
                ["x-secret-key"] = secretKey
            };

            try
            {
                Install(target, url, headers);
                Console.WriteLine("Installed Isis MCP server 'isis' -> " + url);
                Console.WriteLine("  config: " + target);
                Console.WriteLine("  x-access-key: " + Mask(accessKey));
                Console.WriteLine("  x-secret-key: " + Mask(secretKey));
                Console.WriteLine("Restart your agent client to pick up the change.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Install failed: " + e.Message);
                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static string ResolveTarget(bool project)
        {
            if (project) return Path.Combine(Directory.GetCurrentDirectory(), ".mcp.json");

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string claudeJson = Path.Combine(home, ".claude.json");
            if (File.Exists(claudeJson)) return claudeJson;

            string claudeSettings = Path.Combine(home, ".claude", "settings.json");
            if (File.Exists(claudeSettings)) return claudeSettings;

            return claudeJson;
        }

        /// <summary>
        /// Upsert the "isis" MCP entry into the given client config file, preserving all other servers and
        /// unknown keys, and writing a backup of any existing file.
        /// </summary>
        /// <param name="target">The config file path.</param>
        /// <param name="url">The MCP endpoint URL.</param>
        /// <param name="headers">The auth headers to write (e.g. x-access-key and x-secret-key).</param>
        public static void Install(string target, string url, Dictionary<string, string> headers)
        {
            if (headers == null) throw new ArgumentNullException(nameof(headers));

            JsonObject root;
            if (File.Exists(target))
            {
                File.Copy(target, target + ".bak", true);
                string existing = File.ReadAllText(target);
                root = string.IsNullOrWhiteSpace(existing)
                    ? new JsonObject()
                    : (JsonNode.Parse(existing) as JsonObject ?? new JsonObject());
            }
            else
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(target));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                root = new JsonObject();
            }

            JsonObject servers = root["mcpServers"] as JsonObject ?? new JsonObject();

            McpServerEntry entry = new McpServerEntry();
            entry.Type = "http";
            entry.Url = url;
            entry.Headers = new Dictionary<string, string>(headers);

            servers["isis"] = JsonNode.Parse(JsonSerializer.Serialize(entry));
            root["mcpServers"] = servers;

            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(target, root.ToJsonString(options));
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return "(none)";
            if (value.Length <= 4) return "****";
            return new string('*', value.Length - 4) + value.Substring(value.Length - 4);
        }

        #endregion
    }
}
