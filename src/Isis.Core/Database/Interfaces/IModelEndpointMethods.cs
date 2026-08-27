namespace Isis.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Enums;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for model endpoints (embedding and inference).
    /// </summary>
    public interface IModelEndpointMethods
    {
        /// <summary>
        /// Create an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created endpoint.</returns>
        Task<ModelEndpoint> CreateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Read an endpoint by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The endpoint, or null if not found.</returns>
        Task<ModelEndpoint?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate endpoints within a tenant, optionally filtered by kind.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="kind">The optional kind filter; null for all kinds.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<ModelEndpoint>> EnumerateAsync(string tenantId, EndpointKindEnum? kind, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated endpoint.</returns>
        Task<ModelEndpoint> UpdateAsync(ModelEndpoint endpoint, CancellationToken token = default);

        /// <summary>
        /// Delete an endpoint by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The endpoint identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
