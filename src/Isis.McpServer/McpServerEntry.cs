namespace Isis.McpServer
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A single MCP server entry as written into an agent client's configuration file.
    /// </summary>
    public class McpServerEntry
    {
        #region Public-Members

        /// <summary>
        /// The transport type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "http";

        /// <summary>
        /// The MCP endpoint URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The headers sent on each request (the Isis auth header).
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an MCP server entry.
        /// </summary>
        public McpServerEntry()
        {
        }

        #endregion
    }
}
