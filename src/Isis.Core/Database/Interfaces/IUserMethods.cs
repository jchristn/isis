namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for users.
    /// </summary>
    public interface IUserMethods
    {
        /// <summary>
        /// Create a user.
        /// </summary>
        /// <param name="user">The user to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created user.</returns>
        Task<User> CreateAsync(User user, CancellationToken token = default);

        /// <summary>
        /// Read a user by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The user identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The user, or null if not found.</returns>
        Task<User?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a user by email address within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="email">The email address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The user, or null if not found.</returns>
        Task<User?> ReadByEmailAsync(string tenantId, string email, CancellationToken token = default);

        /// <summary>
        /// Enumerate all users with the given email address across every tenant. Email is unique only within a
        /// tenant, so an address may resolve to one user in each of several tenants. Used by the pre-auth
        /// tenant-discovery step of email/password login.
        /// </summary>
        /// <param name="email">The email address.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching users, one per tenant at most; empty when none match.</returns>
        Task<List<User>> EnumerateByEmailAsync(string email, CancellationToken token = default);

        /// <summary>
        /// Enumerate users within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<User>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a user.
        /// </summary>
        /// <param name="user">The user to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated user.</returns>
        Task<User> UpdateAsync(User user, CancellationToken token = default);

        /// <summary>
        /// Delete a user by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The user identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read multiple users by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The user identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching users; empty when none match.</returns>
        Task<List<User>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);

        /// <summary>
        /// Create multiple users.
        /// </summary>
        /// <param name="items">The users to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created users.</returns>
        Task<List<User>> CreateManyAsync(IReadOnlyCollection<User> items, CancellationToken token = default);

        /// <summary>
        /// Delete multiple users by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The user identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of identifiers requested for deletion.</returns>
        Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);
    }
}
