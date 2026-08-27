namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for access-control permissions.
    /// </summary>
    public interface IPermissionMethods
    {
        /// <summary>
        /// Create a permission.
        /// </summary>
        /// <param name="permission">The permission to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created permission.</returns>
        Task<Permission> CreateAsync(Permission permission, CancellationToken token = default);

        /// <summary>
        /// Read a permission by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The permission, or null if not found.</returns>
        Task<Permission?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// List all active permissions for a user within a tenant, used for authorization evaluation.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="userId">The user identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The user's permissions.</returns>
        Task<List<Permission>> ListForUserAsync(string tenantId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate permissions within a tenant, optionally filtered by user.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="userId">An optional user filter; null enumerates all users in the tenant.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Permission>> EnumerateAsync(string tenantId, string? userId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete a permission by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The permission identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
