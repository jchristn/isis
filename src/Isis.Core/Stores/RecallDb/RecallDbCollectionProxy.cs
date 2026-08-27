namespace Isis.Core.Stores.RecallDb
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using global::RecallDb.Sdk;
    using global::RecallDb.Sdk.Models;

    /// <summary>
    /// A thin pass-through to RecallDB's collection management REST API, used by the Isis dashboard so that
    /// collection administration is proxied to RecallDB rather than re-implemented.
    /// </summary>
    public class RecallDbCollectionProxy
    {
        #region Private-Members

        private readonly RecallDbClient _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a collection proxy.
        /// </summary>
        /// <param name="endpoint">The RecallDB endpoint.</param>
        /// <param name="adminKey">The RecallDB admin key.</param>
        /// <exception cref="ArgumentException">Thrown when endpoint or key is missing.</exception>
        public RecallDbCollectionProxy(string endpoint, string adminKey)
        {
            if (string.IsNullOrEmpty(endpoint)) throw new ArgumentException("A RecallDB endpoint is required.", nameof(endpoint));
            if (string.IsNullOrEmpty(adminKey)) throw new ArgumentException("A RecallDB admin key is required.", nameof(adminKey));
            _Client = new RecallDbClient(endpoint, adminKey);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List the collections in a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="maxResults">The maximum number of collections to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collections.</returns>
        public async Task<List<CollectionMetadata>> ListAsync(string tenantId, int maxResults, CancellationToken token = default)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = maxResults < 1 ? 100 : maxResults };
            EnumerationResult<CollectionMetadata> result = await _Client.EnumerateCollectionsAsync(tenantId, query, token).ConfigureAwait(false);
            return result.Objects ?? new List<CollectionMetadata>();
        }

        /// <summary>
        /// Read a collection.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="collectionId">The collection identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The collection.</returns>
        public async Task<CollectionMetadata> ReadAsync(string tenantId, string collectionId, CancellationToken token = default)
        {
            return await _Client.GetCollectionAsync(tenantId, collectionId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a collection.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="name">The collection name.</param>
        /// <param name="dimensionality">The vector dimensionality.</param>
        /// <param name="description">An optional description.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created collection.</returns>
        public async Task<CollectionMetadata> CreateAsync(string tenantId, string name, int dimensionality, string? description, CancellationToken token = default)
        {
            CollectionMetadata collection = new CollectionMetadata
            {
                TenantId = tenantId,
                Name = name,
                Description = description,
                Dimensionality = dimensionality,
                Active = true
            };
            return await _Client.CreateCollectionAsync(tenantId, collection, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a collection.
        /// </summary>
        /// <param name="tenantId">The tenant identifier.</param>
        /// <param name="collectionId">The collection identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string tenantId, string collectionId, CancellationToken token = default)
        {
            await _Client.DeleteCollectionAsync(tenantId, collectionId, token).ConfigureAwait(false);
        }

        #endregion
    }
}
