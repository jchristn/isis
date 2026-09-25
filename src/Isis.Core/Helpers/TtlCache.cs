namespace Isis.Core.Helpers
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A small thread-safe cache whose entries expire after a fixed time to live. Used for hot per-request lookups
    /// (credentials, users, scopes, endpoints) that change rarely. Values are shared between callers, so treat them
    /// as read-only.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    public class TtlCache<T> where T : class
    {
        #region Public-Members

        /// <summary>
        /// Time to live for new entries. Default 10 seconds. Zero or negative disables caching (every lookup loads).
        /// </summary>
        public TimeSpan TimeToLive { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Number of entries currently held (including expired entries not yet evicted).
        /// </summary>
        public int Count
        {
            get
            {
                return _Entries.Count;
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, TtlCacheEntry<T>> _Entries = new ConcurrentDictionary<string, TtlCacheEntry<T>>(StringComparer.Ordinal);
        private long _Generation = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return the cached value for a key, loading and caching it when absent or expired. A null result is not
        /// cached, so a missing record is looked up again next time. A load that started before
        /// <see cref="Clear"/> or <see cref="Remove"/> is not cached, so an invalidation is never undone by a
        /// concurrent reader.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="load">Loads the value on a miss.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The value, or null when the loader returns null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when key or load is null.</exception>
        public async Task<T?> GetOrLoadAsync(string key, Func<CancellationToken, Task<T?>> load, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(load);

            if (TimeToLive <= TimeSpan.Zero) return await load(token).ConfigureAwait(false);

            DateTime now = DateTime.UtcNow;
            if (_Entries.TryGetValue(key, out TtlCacheEntry<T>? entry) && entry.ExpiresUtc > now) return entry.Value;

            long generation = Interlocked.Read(ref _Generation);
            T? loaded = await load(token).ConfigureAwait(false);
            if (loaded != null && Interlocked.Read(ref _Generation) == generation)
            {
                _Entries[key] = new TtlCacheEntry<T>(loaded, DateTime.UtcNow.Add(TimeToLive));
            }

            return loaded;
        }

        /// <summary>
        /// Store a value for a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Set(string key, T value)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            if (TimeToLive <= TimeSpan.Zero) return;
            _Entries[key] = new TtlCacheEntry<T>(value, DateTime.UtcNow.Add(TimeToLive));
        }

        /// <summary>
        /// Remove one key.
        /// </summary>
        /// <param name="key">The key.</param>
        public void Remove(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            Interlocked.Increment(ref _Generation);
            _Entries.TryRemove(key, out _);
        }

        /// <summary>
        /// Remove every entry.
        /// </summary>
        public void Clear()
        {
            Interlocked.Increment(ref _Generation);
            _Entries.Clear();
        }

        #endregion
    }
}
