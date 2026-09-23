namespace Isis.Core.Enums
{
    /// <summary>
    /// Where a resolved (effective) instruction originated, for display and diagnostics.
    /// </summary>
    public enum InstructionSourceEnum
    {
        /// <summary>
        /// A tenant-global instruction, surfaced as-is.
        /// </summary>
        Global,

        /// <summary>
        /// A tenant-global instruction whose content was replaced by a scope-specific instruction.
        /// </summary>
        ScopeOverride,

        /// <summary>
        /// A scope-specific instruction with no tenant-global counterpart (appended).
        /// </summary>
        ScopeAdded
    }
}
