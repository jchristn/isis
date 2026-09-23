namespace Isis.Core.Models
{
    using Isis.Core.Enums;

    /// <summary>
    /// An effective instruction produced by merging a scope's instructions onto the tenant-global set. Carries
    /// the content an agent should see plus provenance (<see cref="Source"/>) for the management UI.
    /// </summary>
    public class ResolvedInstruction
    {
        #region Public-Members

        /// <summary>
        /// The identifier of the underlying instruction that supplied the effective content (the scope-specific
        /// one for an override or an added instruction; otherwise the tenant-global one).
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The instruction name (the merge key).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The effective content conveyed to the agent.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// The effective ordering position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Where the effective content came from.
        /// </summary>
        public InstructionSourceEnum Source { get; set; } = InstructionSourceEnum.Global;

        /// <summary>
        /// The scope this resolution was computed for; null when resolving the tenant-global set alone.
        /// </summary>
        public string? ScopeId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a resolved instruction.
        /// </summary>
        public ResolvedInstruction()
        {
        }

        #endregion
    }
}
