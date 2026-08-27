namespace Isis.Core.Stores.Filesystem
{
    using System;

    /// <summary>
    /// A parsed memory block from a filesystem-backed store file.
    /// </summary>
    internal class MemoryBlock
    {
        #region Internal-Members

        /// <summary>
        /// The memory slug.
        /// </summary>
        internal string Slug { get; set; } = String.Empty;

        /// <summary>
        /// The owning category identifier.
        /// </summary>
        internal string CategoryId { get; set; } = String.Empty;

        /// <summary>
        /// The memory title, if any.
        /// </summary>
        internal string? Title { get; set; } = null;

        /// <summary>
        /// The memory body.
        /// </summary>
        internal string Body { get; set; } = String.Empty;

        /// <summary>
        /// The store key (file path, optionally with a fragment) identifying this block.
        /// </summary>
        internal string StoreKey { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        internal MemoryBlock()
        {
        }

        #endregion
    }
}
