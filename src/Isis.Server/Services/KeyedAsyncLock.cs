namespace Isis.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An async mutual-exclusion lock per string key, with reference counting so entries are removed once no caller
    /// holds or waits on them (the key space, for example one key per memory, is unbounded). Use as
    /// <c>await WaitAsync(key); try { ... } finally { Release(key); }</c>. Process-local: it serializes work within
    /// one Isis node, not across nodes.
    /// </summary>
    public class KeyedAsyncLock
    {
        #region Private-Members

        private readonly Dictionary<string, SemaphoreSlim> _Gates = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _References = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly object _Sync = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Acquire the lock for a key. Every successful call must be paired with <see cref="Release"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task that completes when the lock is held.</returns>
        public async Task WaitAsync(string key, CancellationToken token = default)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            SemaphoreSlim gate;
            lock (_Sync)
            {
                if (!_Gates.TryGetValue(key, out SemaphoreSlim? existing))
                {
                    existing = new SemaphoreSlim(1, 1);
                    _Gates[key] = existing;
                    _References[key] = 0;
                }

                _References[key]++;
                gate = existing;
            }

            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                Dereference(key);
                throw;
            }
        }

        /// <summary>
        /// Release a lock acquired with <see cref="WaitAsync"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        public void Release(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            SemaphoreSlim? gate;
            lock (_Sync)
            {
                if (!_Gates.TryGetValue(key, out gate)) throw new InvalidOperationException("The lock for '" + key + "' is not held.");
            }

            gate.Release();
            Dereference(key);
        }

        /// <summary>
        /// Number of keys currently held or waited on (for diagnostics and tests).
        /// </summary>
        /// <returns>The key count.</returns>
        public int ActiveKeys()
        {
            lock (_Sync)
            {
                return _Gates.Count;
            }
        }

        #endregion

        #region Private-Methods

        private void Dereference(string key)
        {
            lock (_Sync)
            {
                int remaining = --_References[key];
                if (remaining == 0)
                {
                    _Gates[key].Dispose();
                    _Gates.Remove(key);
                    _References.Remove(key);
                }
            }
        }

        #endregion
    }
}
