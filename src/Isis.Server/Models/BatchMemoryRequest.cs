namespace Isis.Server.Models
{
    using System.Collections.Generic;
    using Isis.Core.Models;

    /// <summary>
    /// A request carrying a list of memories, used by the uniform batch upsert endpoint.
    /// </summary>
    public class BatchMemoryRequest
    {
        #region Public-Members

        /// <summary>
        /// The memories to create or update.
        /// </summary>
        public List<Memory> Items { get; set; } = new List<Memory>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a batch memory request.
        /// </summary>
        public BatchMemoryRequest()
        {
        }

        #endregion
    }
}
