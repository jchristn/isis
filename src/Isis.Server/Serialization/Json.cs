namespace Isis.Server.Serialization
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Centralized JSON serialization for API responses and requests. Enums serialize as strings; property
    /// names use camelCase to match the dashboard and SDK expectations.
    /// </summary>
    public static class Json
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Options = BuildOptions();

        #endregion

        #region Public-Members

        /// <summary>
        /// The shared serializer options.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get
            {
                return _Options;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a value to JSON.
        /// </summary>
        /// <param name="value">The value to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string Serialize(object? value)
        {
            return JsonSerializer.Serialize(value, _Options);
        }

        /// <summary>
        /// Deserialize JSON to a typed value.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="json">The JSON string.</param>
        /// <returns>The deserialized value, or null.</returns>
        public static T? Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.WriteIndented = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
