namespace Isis.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for credentials.
    /// </summary>
    public interface ICredentialMethods
    {
        /// <summary>
        /// Create a credential.
        /// </summary>
        /// <param name="credential">The credential to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created credential.</returns>
        Task<Credential> CreateAsync(Credential credential, CancellationToken token = default);

        /// <summary>
        /// Read a credential by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The credential, or null if not found.</returns>
        Task<Credential?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a credential by its public access key.
        /// </summary>
        /// <param name="accessKey">The access key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The credential, or null if not found.</returns>
        Task<Credential?> ReadByAccessKeyAsync(string accessKey, CancellationToken token = default);

        /// <summary>
        /// Enumerate credentials within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Credential>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a credential.
        /// </summary>
        /// <param name="credential">The credential to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated credential.</returns>
        Task<Credential> UpdateAsync(Credential credential, CancellationToken token = default);

        /// <summary>
        /// Delete a credential by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
