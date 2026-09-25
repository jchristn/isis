namespace Isis.Core.Helpers
{
    using System;

    /// <summary>
    /// A cached value and when it expires.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    public class TtlCacheEntry<T> where T : class
    {
        #region Public-Members

        /// <summary>
        /// The cached value.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// When the entry stops being served, in UTC.
        /// </summary>
        public DateTime ExpiresUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="expiresUtc">Expiry time in UTC.</param>
        public TtlCacheEntry(T value, DateTime expiresUtc)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ExpiresUtc = expiresUtc;
        }

        #endregion
    }
}
