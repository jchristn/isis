namespace Isis.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Hashes and verifies user passwords, and compares secret-equivalent material in constant time.
    /// Passwords are stored as a lowercase hex-encoded SHA-256 digest.
    /// </summary>
    public static class PasswordHasher
    {
        #region Public-Methods

        /// <summary>
        /// Hash a plaintext password to a lowercase hex-encoded SHA-256 digest.
        /// </summary>
        /// <param name="password">The plaintext password.</param>
        /// <returns>The hex-encoded SHA-256 digest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the password is null or empty.</exception>
        public static string Hash(string password)
        {
            if (String.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// Verify a plaintext password against a stored hex-encoded SHA-256 hash in constant time.
        /// </summary>
        /// <param name="password">The presented plaintext password.</param>
        /// <param name="hash">The stored hex-encoded SHA-256 hash.</param>
        /// <returns>True when the password matches the hash.</returns>
        public static bool Verify(string password, string? hash)
        {
            if (String.IsNullOrEmpty(password) || String.IsNullOrEmpty(hash)) return false;
            return FixedTimeEquals(Hash(password), hash);
        }

        /// <summary>
        /// Compare two strings in constant time using their UTF-8 byte representations.
        /// </summary>
        /// <param name="a">First value.</param>
        /// <param name="b">Second value.</param>
        /// <returns>True when the values are equal.</returns>
        public static bool FixedTimeEquals(string? a, string? b)
        {
            if (a == null || b == null) return false;
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(ba, bb);
        }

        #endregion
    }
}
