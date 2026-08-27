namespace Isis.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for request history entries.
    /// </summary>
    public interface IRequestHistoryMethods
    {
        /// <summary>
        /// Create a request history entry.
        /// </summary>
        /// <param name="entry">The entry to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created entry.</returns>
        Task<RequestHistoryEntry> CreateAsync(RequestHistoryEntry entry, CancellationToken token = default);

        /// <summary>
        /// Read a request history entry by identifier.
        /// </summary>
        /// <param name="id">The entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null if not found.</returns>
        Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate request history entries, optionally scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant filter; null enumerates across all tenants (admin only).</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(string? tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete request history entries, optionally scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant filter; null clears across all tenants (admin only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of entries deleted.</returns>
        Task<long> DeleteAllAsync(string? tenantId, CancellationToken token = default);
    }
}
