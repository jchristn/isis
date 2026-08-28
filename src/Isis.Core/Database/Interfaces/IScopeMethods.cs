namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for scopes.
    /// </summary>
    public interface IScopeMethods
    {
        /// <summary>
        /// Create a scope.
        /// </summary>
        /// <param name="scope">The scope to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created scope.</returns>
        Task<Scope> CreateAsync(Scope scope, CancellationToken token = default);

        /// <summary>
        /// Read a scope by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The scope identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope, or null if not found.</returns>
        Task<Scope?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a scope by name within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="name">The scope name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scope, or null if not found.</returns>
        Task<Scope?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default);

        /// <summary>
        /// Enumerate scopes within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Scope>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a scope.
        /// </summary>
        /// <param name="scope">The scope to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated scope.</returns>
        Task<Scope> UpdateAsync(Scope scope, CancellationToken token = default);

        /// <summary>
        /// Delete a scope by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The scope identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read multiple scopes by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The scope identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching scopes; empty when none match.</returns>
        Task<List<Scope>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);

        /// <summary>
        /// Create multiple scopes.
        /// </summary>
        /// <param name="items">The scopes to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created scopes.</returns>
        Task<List<Scope>> CreateManyAsync(IReadOnlyCollection<Scope> items, CancellationToken token = default);

        /// <summary>
        /// Delete multiple scopes by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The scope identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of identifiers requested for deletion.</returns>
        Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);
    }
}
