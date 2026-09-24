namespace Isis.Core.Helpers
{
    using System.Text;

    /// <summary>
    /// Cleans text before it reaches tokenizers and stores. Thread-safe (stateless).
    /// </summary>
    public static class TextSanitizer
    {
        #region Public-Methods

        /// <summary>
        /// Replace unpaired UTF-16 surrogates with U+FFFD (the Unicode replacement character). A lone surrogate is not
        /// valid Unicode: .NET normalization throws on it, and PostgreSQL cannot store it as UTF-8, so a single one would
        /// otherwise make the whole memory unstorable. Valid text, including surrogate pairs (emoji and other astral
        /// characters), is returned unchanged.
        /// </summary>
        /// <param name="value">The text. May be null.</param>
        /// <returns>The text with unpaired surrogates replaced, or the input when it has none (null stays null).</returns>
        public static string? ReplaceInvalidSurrogates(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (!HasUnpairedSurrogate(value)) return value;

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder.Append(c).Append(value[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    builder.Append('�');
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static bool HasUnpairedSurrogate(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                if (char.IsSurrogate(c)) return true;
            }

            return false;
        }

        #endregion
    }
}
