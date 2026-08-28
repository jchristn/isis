namespace Isis.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A request carrying a list of identifiers, used by the uniform batch-get and batch-delete endpoints.
    /// </summary>
    public class BatchIdsRequest
    {
        #region Public-Members

        /// <summary>
        /// The identifiers to operate on.
        /// </summary>
        public List<string> Ids { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a batch identifiers request.
        /// </summary>
        public BatchIdsRequest()
        {
        }

        #endregion
    }
}
