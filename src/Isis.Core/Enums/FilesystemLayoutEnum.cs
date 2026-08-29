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
        Hierarchy,

        /// <summary>
        /// An Open Knowledge Format (OKF) bundle: one markdown file per memory under a
        /// category/slug directory hierarchy, each with a YAML frontmatter block (the OKF core
        /// fields plus Isis provenance), and a generated root index.md. The target-path directory
        /// is a valid, git-trackable OKF bundle at all times — no separate export step.
        /// </summary>
        OkfBundle
    }
}
