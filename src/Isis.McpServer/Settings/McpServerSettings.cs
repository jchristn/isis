namespace Isis.McpServer.Settings
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Settings for the Isis MCP server: its own transport, and the Isis REST API it proxies.
    /// </summary>
    public class McpServerSettings
    {
        #region Public-Members

        /// <summary>
        /// The MCP transport hostname to bind. Default 127.0.0.1.
        /// </summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// The MCP transport port. Default 8720.
        /// </summary>
        public int Port { get; set; } = 8720;

        /// <summary>
        /// The JSON-RPC path. Default /rpc.
        /// </summary>
        public string RpcPath { get; set; } = "/rpc";

        /// <summary>
        /// The SSE events path. Default /events.
        /// </summary>
        public string EventsPath { get; set; } = "/events";

        /// <summary>
        /// The streamable-HTTP MCP path. Default /mcp.
        /// </summary>
        public string McpPath { get; set; } = "/mcp";

        /// <summary>
        /// The Isis REST API hostname to proxy to. Default 127.0.0.1.
        /// </summary>
        public string RestHostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// The Isis REST API port. Default 8700.
        /// </summary>
        public int RestPort { get; set; } = 8700;

        /// <summary>
        /// Whether the Isis REST API uses TLS.
        /// </summary>
        public bool RestSsl { get; set; } = false;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = BuildOptions();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default settings.
        /// </summary>
        public McpServerSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The base URL of the Isis REST API.
        /// </summary>
        /// <returns>The base URL.</returns>
        public string RestBaseUrl()
        {
            string scheme = RestSsl ? "https" : "http";
            return scheme + "://" + RestHostname + ":" + RestPort;
        }

        /// <summary>
        /// Load settings from a JSON file, returning defaults when it does not exist.
        /// </summary>
        /// <param name="path">The settings file path.</param>
        /// <returns>The loaded or default settings.</returns>
        public static McpServerSettings FromFile(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return new McpServerSettings();
            string json = File.ReadAllText(path);
            if (String.IsNullOrWhiteSpace(json)) return new McpServerSettings();
            McpServerSettings? settings = JsonSerializer.Deserialize<McpServerSettings>(json, _Options);
            return settings ?? new McpServerSettings();
        }

        /// <summary>
        /// Persist settings to a JSON file.
        /// </summary>
        /// <param name="path">The settings file path.</param>
        public void ToFile(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(this, _Options));
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.WriteIndented = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            return options;
        }

        #endregion
    }
}
