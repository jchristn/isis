namespace Isis.Core.Enums
{
    /// <summary>
    /// How a search treats memories that another memory has replaced (see <c>Memory.Supersedes</c>).
    /// </summary>
    public enum SupersededHandlingEnum
    {
        /// <summary>
        /// Keep replaced memories but rank each one directly after its replacement, and add the replacement when the
        /// search did not retrieve it. The replaced hit is marked with <c>supersededBy</c>. The default.
        /// </summary>
        Demote,

        /// <summary>
        /// Drop replaced memories, adding their replacement when the search did not retrieve it.
        /// </summary>
        Hide,

        /// <summary>
        /// Leave the ranking unchanged; replaced hits are only marked with <c>supersededBy</c>.
        /// </summary>
        Include
    }
}
