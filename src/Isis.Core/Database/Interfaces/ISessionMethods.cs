namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for authentication sessions.
    /// </summary>
    public interface ISessionMethods
    {
        /// <summary>
        /// Create a session.
        /// </summary>
        /// <param name="session">The session to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created session.</returns>
        Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default);

        /// <summary>
        /// Read a session by identifier.
        /// </summary>
        /// <param name="id">The session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The session, or null if not found.</returns>
        Task<AuthSession?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a session by its bearer token.
        /// </summary>
        /// <param name="tokenValue">The bearer token value.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The session, or null if not found.</returns>
        Task<AuthSession?> ReadByTokenAsync(string tokenValue, CancellationToken token = default);

        /// <summary>
        /// Enumerate sessions within a tenant.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<AuthSession>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a session.
        /// </summary>
        /// <param name="session">The session to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated session.</returns>
        Task<AuthSession> UpdateAsync(AuthSession session, CancellationToken token = default);

        /// <summary>
        /// Delete a session by identifier.
        /// </summary>
        /// <param name="id">The session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read multiple sessions by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The session identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching sessions; empty when none match.</returns>
        Task<List<AuthSession>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);

        /// <summary>
        /// Create multiple sessions.
        /// </summary>
        /// <param name="items">The sessions to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created sessions.</returns>
        Task<List<AuthSession>> CreateManyAsync(IReadOnlyCollection<AuthSession> items, CancellationToken token = default);

        /// <summary>
        /// Delete multiple sessions by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The session identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of identifiers requested for deletion.</returns>
        Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);
    }
}
