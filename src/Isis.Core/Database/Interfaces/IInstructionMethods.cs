namespace Isis.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for tenant-scoped agent instructions.
    /// </summary>
    public interface IInstructionMethods
    {
        /// <summary>
        /// Create an instruction.
        /// </summary>
        /// <param name="instruction">The instruction to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created instruction.</returns>
        Task<Instruction> CreateAsync(Instruction instruction, CancellationToken token = default);

        /// <summary>
        /// Read an instruction by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The instruction identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The instruction, or null if not found.</returns>
        Task<Instruction?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate instructions within a tenant, ordered by ascending position.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Instruction>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update an instruction.
        /// </summary>
        /// <param name="instruction">The instruction to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated instruction.</returns>
        Task<Instruction> UpdateAsync(Instruction instruction, CancellationToken token = default);

        /// <summary>
        /// Delete an instruction by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The instruction identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read multiple instructions by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The instruction identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching instructions; empty when none match.</returns>
        Task<List<Instruction>> ReadManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);

        /// <summary>
        /// Create multiple instructions.
        /// </summary>
        /// <param name="items">The instructions to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created instructions.</returns>
        Task<List<Instruction>> CreateManyAsync(IReadOnlyCollection<Instruction> items, CancellationToken token = default);

        /// <summary>
        /// Delete multiple instructions by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="ids">The instruction identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of identifiers requested for deletion.</returns>
        Task<int> DeleteManyAsync(string tenantId, IReadOnlyCollection<string> ids, CancellationToken token = default);
    }
}
