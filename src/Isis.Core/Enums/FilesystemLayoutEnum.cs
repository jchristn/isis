namespace Isis.Core.Enums
{
    /// <summary>
    /// Sub-mode for the filesystem memory store, controlling how memories are laid out on disk.
    /// </summary>
    public enum FilesystemLayoutEnum
    {
        /// <summary>
        /// One large file containing all memories as delimited sections.
        /// </summary>
        SingleFile,

        /// <summary>
        /// An organized hierarchy of files (one file per memory) rooted at the target path,
        /// with a generated index.
        /// </summary>
        Hierarchy
    }
}
