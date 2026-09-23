namespace Isis.Core.Enums
{
    /// <summary>
    /// How a scope-specific instruction combines with the tenant-global instruction set (matched by name).
    /// </summary>
    public enum InstructionMergeModeEnum
    {
        /// <summary>
        /// Add the instruction to the effective set (after the tenant-global instructions).
        /// </summary>
        Append,

        /// <summary>
        /// Replace the content of a same-named tenant-global instruction, keeping its position. Behaves like
        /// <see cref="Append"/> when no tenant-global instruction shares the name.
        /// </summary>
        Replace,

        /// <summary>
        /// Suppress a same-named tenant-global instruction from the effective set.
        /// </summary>
        Hide
    }
}
