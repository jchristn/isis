namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for the memory index (the Isis-owned metadata rows that point at memory-store content).
    /// </summary>
    public interface IMemoryIndexMethods
    {
        /// <summary>
        /// Create a memory index row.
        /// </summary>
        /// <param name="memory">The memory to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created memory.</returns>
        Task<Memory> CreateAsync(Memory memory, CancellationToken token = default);

        /// <summary>
        /// Read a memory by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The memory identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The memory, or null if not found.</returns>
        Task<Memory?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a memory by its slug within a (scope, category).
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="scopeId">The owning scope identifier.</param>
        /// <param name="categoryId">The owning category identifier.</param>
        /// <param name="slug">The memory slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The memory, or null if not found.</returns>
        Task<Memory?> ReadBySlugAsync(string tenantId, string scopeId, string categoryId, string slug, CancellationToken token = default);

        /// <summary>
        /// Enumerate memories within a scope, optionally filtered by category.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="scopeId">The owning scope identifier.</param>
        /// <param name="categoryId">The optional category filter; null for all categories.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Memory>> EnumerateAsync(string tenantId, string scopeId, string? categoryId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a memory index row.
        /// </summary>
        /// <param name="memory">The memory to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated memory.</returns>
        Task<Memory> UpdateAsync(Memory memory, CancellationToken token = default);

        /// <summary>
        /// Delete a memory by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The memory identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read multiple memories by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The memory identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching memories; empty when none match.</returns>
        Task<List<Memory>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);

        /// <summary>
        /// Create multiple memory index rows.
        /// </summary>
        /// <param name="items">The memories to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created memories.</returns>
        Task<List<Memory>> CreateManyAsync(IReadOnlyCollection<Memory> items, CancellationToken token = default);

        /// <summary>
        /// Delete multiple memories by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The memory identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of identifiers requested for deletion.</returns>
        Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);
    }
}
