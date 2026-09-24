namespace Isis.Core.Stores.RecallDb
{
    using System;
    using System.Collections.Concurrent;
    using global::RecallDb.Sdk;

    /// <summary>
    /// Process-wide cache of <see cref="RecallDbClient"/> instances, one per endpoint and key. Stores are created per
    /// request, and each RecallDbClient owns an HttpClient: constructing one per request opened a new connection pool
    /// (and TCP connection) for every store call and never disposed it. Sharing a client reuses pooled connections.
    /// RecallDbClient is safe for concurrent use (its HttpClient is configured once in the constructor).
    /// </summary>
    public static class RecallDbClientPool
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<string, RecallDbClient> _Clients = new ConcurrentDictionary<string, RecallDbClient>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the shared client for an endpoint and key, creating it on first use.
        /// </summary>
        /// <param name="endpoint">The RecallDB server endpoint.</param>
        /// <param name="adminKey">The RecallDB admin/bearer key.</param>
        /// <returns>The shared client.</returns>
        /// <exception cref="ArgumentException">Thrown when endpoint or key is missing.</exception>
        public static RecallDbClient Get(string endpoint, string adminKey)
        {
            if (string.IsNullOrEmpty(endpoint)) throw new ArgumentException("A RecallDB endpoint is required.", nameof(endpoint));
            if (string.IsNullOrEmpty(adminKey)) throw new ArgumentException("A RecallDB admin key is required.", nameof(adminKey));
            return _Clients.GetOrAdd(endpoint + "\n" + adminKey, _ => new RecallDbClient(endpoint, adminKey));
        }

        #endregion
    }
}
