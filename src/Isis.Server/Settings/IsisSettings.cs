namespace Isis.Server.Settings
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Isis.Core.Database;

    /// <summary>
    /// Root server settings, loaded from a JSON file and overridable by environment variables.
    /// </summary>
    public class IsisSettings
    {
        #region Public-Members

        /// <summary>
        /// The node identifier for this server instance.
        /// </summary>
        public string NodeId { get; set; } = "isis-1";

        /// <summary>
        /// REST listener settings.
        /// </summary>
        public RestSettings Rest { get; set; } = new RestSettings();

        /// <summary>
        /// Relational metadata store settings.
        /// </summary>
        public DatabaseSettings Database { get; set; } = new DatabaseSettings();

        /// <summary>
        /// RecallDB integration settings.
        /// </summary>
        public RecallDbSettings RecallDb { get; set; } = new RecallDbSettings();

        /// <summary>
        /// Verbex integration settings.
        /// </summary>
        public VerbexSettings Verbex { get; set; } = new VerbexSettings();

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public AuthSettings Auth { get; set; } = new AuthSettings();

        /// <summary>
        /// Logging settings.
        /// </summary>
        public LoggingSettings Logging { get; set; } = new LoggingSettings();

        /// <summary>
        /// Request history capture settings.
        /// </summary>
        public RequestHistorySettings RequestHistory { get; set; } = new RequestHistorySettings();

        /// <summary>
        /// Observability (metrics and tracing) settings.
        /// </summary>
        public ObservabilitySettings Observability { get; set; } = new ObservabilitySettings();

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = BuildOptions();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default settings.
        /// </summary>
        public IsisSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load settings from a JSON file, returning defaults when the file does not exist.
        /// </summary>
        /// <param name="path">The settings file path.</param>
        /// <returns>The loaded or default settings.</returns>
        public static IsisSettings FromFile(string path)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return new IsisSettings();
            string json = File.ReadAllText(path);
            if (String.IsNullOrWhiteSpace(json)) return new IsisSettings();
            IsisSettings? settings = JsonSerializer.Deserialize<IsisSettings>(json, _Options);
            return settings ?? new IsisSettings();
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
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
