namespace Isis.Server.Models
{
    using System.Collections.Generic;
    using Isis.Core.Models;

    /// <summary>
    /// A request carrying a list of instructions, used by the uniform batch create endpoint.
    /// </summary>
    public class BatchInstructionRequest
    {
        #region Public-Members

        /// <summary>
        /// The instructions to create.
        /// </summary>
        public List<Instruction> Items { get; set; } = new List<Instruction>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a batch instruction request.
        /// </summary>
        public BatchInstructionRequest()
        {
        }

        #endregion
    }
}
