namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Helpers;

    /// <summary>
    /// A tenant-scoped instruction surfaced to agents over MCP (via the isis_instructions tool). Instructions
    /// convey how an agent should use this tenant's memory — conventions, house rules, and standing guidance —
    /// and are returned in ascending Position order.
    /// </summary>
    public class Instruction
    {
        #region Public-Members

        /// <summary>
        /// Instruction identifier. Defaults to a generated value; may not be set to null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier. May not be set to null or empty.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// Human-readable instruction name. May not be set to null or empty.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// The instruction content conveyed to the agent.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Ordering position; instructions are returned to agents in ascending order.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Indicates whether the instruction is active. Only active instructions are surfaced to agents.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Indicates whether the instruction is protected from deletion.
        /// </summary>
        public bool Protected { get; set; } = false;

        /// <summary>
        /// UTC timestamp when the instruction was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the instruction was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Instruction();
        private string _TenantId = String.Empty;
        private string _Name = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an instruction.
        /// </summary>
        public Instruction()
        {
        }

        #endregion
    }
}
