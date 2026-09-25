namespace Isis.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;

    /// <summary>
    /// Short-lived cache for the records every request looks up: the caller's credential and user, and the target
    /// scope and its embedding endpoint. Writes through this node invalidate immediately (see
    /// <see cref="InvalidateForWrite"/>); the time to live bounds staleness from writes on other nodes. Thread-safe.
    /// Cached objects are shared between requests, so callers must not mutate them.
    /// </summary>
    public class LookupCache
    {
        #region Public-Members

        /// <summary>
        /// Whether caching is active. When false every lookup goes to the database.
        /// </summary>
        public bool Enabled { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly TtlCache<Credential> _Credentials = new TtlCache<Credential>();
        private readonly TtlCache<User> _Users = new TtlCache<User>();
        private readonly TtlCache<Scope> _Scopes = new TtlCache<Scope>();
        private readonly TtlCache<ModelEndpoint> _Endpoints = new TtlCache<ModelEndpoint>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="enabled">Whether to cache.</param>
        /// <param name="timeToLive">Entry time to live.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public LookupCache(DatabaseDriverBase database, bool enabled, TimeSpan timeToLive)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            Enabled = enabled;
            TimeSpan ttl = enabled ? timeToLive : TimeSpan.Zero;
            _Credentials.TimeToLive = ttl;
            _Users.TimeToLive = ttl;
            _Scopes.TimeToLive = ttl;
            _Endpoints.TimeToLive = ttl;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a credential by access key.
        /// </summary>
        /// <param name="accessKey">The access key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The credential, or null.</returns>
        public Task<Credential?> GetCredentialByAccessKeyAsync(string accessKey, CancellationToken token = default)
        {
            return _Credentials.GetOrLoadAsync(accessKey, ct => _Database.Credentials.ReadByAccessKeyAsync(accessKey, ct), token);
        }

        /// <summary>
        /// Read a user.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="userId">User id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The user, or null.</returns>
        public Task<User?> GetUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            return _Users.GetOrLoadAsync(tenantId + "/" + userId, ct => _Database.Users.ReadAsync(tenantId, userId, ct), token);
        }

        /// <summary>
        /// Read a scope. A RecallDB scope whose collection has not been provisioned yet is not cached, because its
        /// first write fills in the collection id and every later request must see it.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="scopeId">Scope id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope, or null.</returns>
        public async Task<Scope?> GetScopeAsync(string tenantId, string scopeId, CancellationToken token = default)
        {
            string key = tenantId + "/" + scopeId;
            Scope? scope = await _Scopes.GetOrLoadAsync(key, ct => _Database.Scopes.ReadAsync(tenantId, scopeId, ct), token).ConfigureAwait(false);
            if (scope != null && scope.StoreProvider == StoreProviderEnum.RecallDb && string.IsNullOrEmpty(scope.RecallCollectionId)) _Scopes.Remove(key);
            return scope;
        }

        /// <summary>
        /// Read a model endpoint.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="endpointId">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint, or null.</returns>
        public Task<ModelEndpoint?> GetEndpointAsync(string tenantId, string endpointId, CancellationToken token = default)
        {
            return _Endpoints.GetOrLoadAsync(tenantId + "/" + endpointId, ct => _Database.ModelEndpoints.ReadAsync(tenantId, endpointId, ct), token);
        }

        /// <summary>
        /// Drop a scope's cached entry (for example after its collection id was filled in).
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <param name="scopeId">Scope id.</param>
        public void InvalidateScope(string tenantId, string scopeId)
        {
            _Scopes.Remove(tenantId + "/" + scopeId);
        }

        /// <summary>
        /// Invalidate whatever a mutating request could have changed, based on its method and path. Called after
        /// every request; reads are ignored. Invalidation is deliberately coarse (a whole record type) because
        /// these writes are rare and correctness matters more than hit rate.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Request path without query.</param>
        public void InvalidateForWrite(string method, string path)
        {
            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path)) return;
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) return;

            string lower = path.ToLowerInvariant();

            // Search, chat and memory writes are the hot path and change none of the cached record types.
            if (lower.Contains("/memories", StringComparison.Ordinal) || lower.EndsWith("/chat", StringComparison.Ordinal) || lower.Contains("/chat/", StringComparison.Ordinal)) return;

            if (lower.Contains("/credentials", StringComparison.Ordinal)) _Credentials.Clear();
            if (lower.Contains("/users", StringComparison.Ordinal))
            {
                _Users.Clear();
                _Credentials.Clear();
            }

            if (lower.Contains("/endpoints", StringComparison.Ordinal)) _Endpoints.Clear();
            if (lower.Contains("/scopes", StringComparison.Ordinal) || lower.Contains("/categories", StringComparison.Ordinal)) _Scopes.Clear();

            // Tenant deletes cascade to everything, and tenant-level settings can affect any record.
            if (lower.Contains("/tenants", StringComparison.Ordinal) && !lower.Contains("/scopes", StringComparison.Ordinal) && !lower.Contains("/endpoints", StringComparison.Ordinal)
                && !lower.Contains("/credentials", StringComparison.Ordinal) && !lower.Contains("/users", StringComparison.Ordinal) && !lower.Contains("/instructions", StringComparison.Ordinal))
            {
                InvalidateAll();
            }
        }

        /// <summary>
        /// Drop every cached entry.
        /// </summary>
        public void InvalidateAll()
        {
            _Credentials.Clear();
            _Users.Clear();
            _Scopes.Clear();
            _Endpoints.Clear();
        }

        #endregion
    }
}
