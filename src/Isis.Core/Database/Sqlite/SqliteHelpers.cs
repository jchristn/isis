namespace Isis.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using Isis.Core.Database.Sqlite.Queries;

    /// <summary>
    /// Shared SQLite value-encoding and parsing helpers used by the entity method implementations.
    /// </summary>
    internal static class SqliteHelpers
    {
        #region Internal-Methods

        /// <summary>
        /// Escape a string for safe inclusion in a single-quoted SQL literal.
        /// </summary>
        /// <param name="input">The input value.</param>
        /// <returns>The escaped value.</returns>
        internal static string Sanitize(string? input)
        {
            return String.IsNullOrEmpty(input) ? String.Empty : input.Replace("'", "''");
        }

        /// <summary>
        /// Render a nullable string as a quoted SQL literal or NULL.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>The SQL fragment.</returns>
        internal static string ToSql(string? value)
        {
            return String.IsNullOrEmpty(value) ? "NULL" : "'" + Sanitize(value) + "'";
        }

        /// <summary>
        /// Render a required string as a quoted SQL literal.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>The SQL fragment.</returns>
        internal static string ToSqlRequired(string? value)
        {
            return "'" + Sanitize(value) + "'";
        }

        /// <summary>
        /// Render a boolean as a SQLite integer literal.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>"1" or "0".</returns>
        internal static string ToSql(bool value)
        {
            return value ? "1" : "0";
        }

        /// <summary>
        /// Render a nullable UTC timestamp as a quoted SQL literal or NULL.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>The SQL fragment.</returns>
        internal static string ToSql(DateTime? value)
        {
            if (!value.HasValue) return "NULL";
            return "'" + value.Value.ToUniversalTime().ToString(SetupQueries.TimestampFormat, CultureInfo.InvariantCulture) + "'";
        }

        /// <summary>
        /// Render a required UTC timestamp as a quoted SQL literal.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>The SQL fragment.</returns>
        internal static string ToSqlRequired(DateTime value)
        {
            return "'" + value.ToUniversalTime().ToString(SetupQueries.TimestampFormat, CultureInfo.InvariantCulture) + "'";
        }

        /// <summary>
        /// Return null for an empty string, otherwise the string.
        /// </summary>
        /// <param name="value">The input value.</param>
        /// <returns>The value or null.</returns>
        internal static string? NullIfEmpty(string? value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Read a string column value from a data row cell.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The string, or empty.</returns>
        internal static string GetString(object? value)
        {
            return value?.ToString() ?? String.Empty;
        }

        /// <summary>
        /// Interpret a SQLite integer column as a boolean.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>True when the value equals 1.</returns>
        internal static bool GetBool(object? value)
        {
            string text = value?.ToString() ?? "0";
            return text == "1" || String.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parse an integer column value.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <param name="fallback">The value to return when parsing fails.</param>
        /// <returns>The parsed integer.</returns>
        internal static int GetInt(object? value, int fallback = 0)
        {
            string text = value?.ToString() ?? String.Empty;
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        /// <summary>
        /// Parse a double column value.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <param name="fallback">The value to return when parsing fails.</param>
        /// <returns>The parsed double.</returns>
        internal static double GetDouble(object? value, double fallback = 0.0)
        {
            string text = value?.ToString() ?? String.Empty;
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
        }

        /// <summary>
        /// Parse a required UTC timestamp column value.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The parsed timestamp, or <see cref="DateTime.MinValue"/> when empty.</returns>
        internal static DateTime ParseTimestamp(object? value)
        {
            string text = value?.ToString() ?? String.Empty;
            if (String.IsNullOrEmpty(text)) return DateTime.MinValue;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        /// <summary>
        /// Parse a nullable UTC timestamp column value.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The parsed timestamp, or null when empty.</returns>
        internal static DateTime? ParseNullableTimestamp(object? value)
        {
            string text = value?.ToString() ?? String.Empty;
            if (String.IsNullOrEmpty(text)) return null;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        /// <summary>
        /// Serialize a string list to a JSON array for storage.
        /// </summary>
        /// <param name="value">The list.</param>
        /// <returns>A JSON array string.</returns>
        internal static string SerializeList(List<string>? value)
        {
            return JsonSerializer.Serialize(value ?? new List<string>());
        }

        /// <summary>
        /// Deserialize a JSON array column value into a string list.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The list, never null.</returns>
        internal static List<string> DeserializeList(object? value)
        {
            string text = value?.ToString() ?? String.Empty;
            if (String.IsNullOrEmpty(text)) return new List<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(text) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Serialize a string dictionary to a JSON object for storage.
        /// </summary>
        /// <param name="value">The dictionary.</param>
        /// <returns>A JSON object string.</returns>
        internal static string SerializeMap(Dictionary<string, string>? value)
        {
            return JsonSerializer.Serialize(value ?? new Dictionary<string, string>());
        }

        /// <summary>
        /// Deserialize a JSON object column value into a string dictionary.
        /// </summary>
        /// <param name="value">The cell value.</param>
        /// <returns>The dictionary, never null.</returns>
        internal static Dictionary<string, string> DeserializeMap(object? value)
        {
            string text = value?.ToString() ?? String.Empty;
            if (String.IsNullOrEmpty(text)) return new Dictionary<string, string>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        #endregion
    }
}
