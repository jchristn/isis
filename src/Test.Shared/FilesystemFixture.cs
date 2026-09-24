namespace Test.Shared
{
    using Isis.Core.Models;
    using Isis.Server.Services;

    /// <summary>
    /// A throwaway filesystem-backed scope, category, and memory service for tests that need a real store without
    /// external services.
    /// </summary>
    internal sealed class FilesystemFixture
    {
        #region Internal-Members

        /// <summary>
        /// Working directory the filesystem store writes to (delete it when done).
        /// </summary>
        internal string Work { get; set; } = string.Empty;

        /// <summary>
        /// The scope.
        /// </summary>
        internal Scope Scope { get; set; } = new Scope();

        /// <summary>
        /// The category.
        /// </summary>
        internal Category Category { get; set; } = new Category();

        /// <summary>
        /// The memory service.
        /// </summary>
        internal MemoryService Service { get; set; } = null!;

        #endregion
    }
}
