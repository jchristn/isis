namespace Isis.Core.Database.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// Data access methods for categories.
    /// </summary>
    public interface ICategoryMethods
    {
        /// <summary>
        /// Create a category.
        /// </summary>
        /// <param name="category">The category to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created category.</returns>
        Task<Category> CreateAsync(Category category, CancellationToken token = default);

        /// <summary>
        /// Read a category by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The category identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The category, or null if not found.</returns>
        Task<Category?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>
        /// Read a category by name within a scope.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="scopeId">The owning scope identifier.</param>
        /// <param name="name">The category name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The category, or null if not found.</returns>
        Task<Category?> ReadByNameAsync(string tenantId, string scopeId, string name, CancellationToken token = default);

        /// <summary>
        /// Enumerate categories within a scope.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="scopeId">The owning scope identifier.</param>
        /// <param name="query">The enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The enumeration result.</returns>
        Task<EnumerationResult<Category>> EnumerateAsync(string tenantId, string scopeId, EnumerationQuery query, CancellationToken token = default);

        /// <summary>
        /// Update a category.
        /// </summary>
        /// <param name="category">The category to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated category.</returns>
        Task<Category> UpdateAsync(Category category, CancellationToken token = default);

        /// <summary>
        /// Delete a category by identifier.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <param name="id">The category identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default);
    }
}
