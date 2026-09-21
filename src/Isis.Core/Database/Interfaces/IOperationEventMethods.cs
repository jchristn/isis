namespace Isis.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for operation events (semantic scope/memory/agentic activity captured for
    /// observability). Managed consistently with request history: append on capture, prune by age.
    /// </summary>
    public interface IOperationEventMethods
    {
        /// <summary>
        /// Create an operation event.
        /// </summary>
        /// <param name="entry">The event to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created event.</returns>
        Task<OperationEvent> CreateAsync(OperationEvent entry, CancellationToken token = default);

        /// <summary>
        /// Read an operation event by identifier.
        /// </summary>
        /// <param name="id">The event identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event, or null if not found.</returns>
        Task<OperationEvent?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate operation events, optionally scoped to a tenant and filtered by resource type and operation.
        /// </summary>
        /// <param name="tenantId">The tenant filter; null enumerates across all tenants (admin only).</param>
        /// <param name="resourceType">Optional resource-type filter (for example "scope", "memory").</param>
        /// <param name="operation">Optional operation filter (for example "create", "delete").</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<OperationEvent>> EnumerateAsync(string? tenantId, string? resourceType, string? operation, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Delete operation events, optionally scoped to a tenant.
        /// </summary>
        /// <param name="tenantId">The tenant filter; null clears across all tenants (admin only).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of events deleted.</returns>
        Task<long> DeleteAllAsync(string? tenantId, CancellationToken token = default);

        /// <summary>
        /// Delete operation events created strictly before the given UTC cutoff. Used by the retention pruner.
        /// </summary>
        /// <param name="cutoffUtc">The UTC cutoff; events older than this are removed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of events deleted.</returns>
        Task<long> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken token = default);
    }
}
