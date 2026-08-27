namespace Isis.Core.Enums
{
    /// <summary>
    /// Classification of a memory, used for ranking hints and filtering.
    /// </summary>
    public enum MemoryTypeEnum
    {
        /// <summary>
        /// A fact about the user (role, preferences, expertise).
        /// </summary>
        User,

        /// <summary>
        /// Guidance on how the agent should work (corrections, confirmed approaches).
        /// </summary>
        Feedback,

        /// <summary>
        /// Ongoing work, goals, or constraints for a project or artifact.
        /// </summary>
        Project,

        /// <summary>
        /// A pointer to an external resource or reference material.
        /// </summary>
        Reference
    }
}
