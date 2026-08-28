namespace Isis.Server.Models
{
    using System.Collections.Generic;
    using Isis.Core.Models;

    /// <summary>
    /// A request carrying a list of model endpoints, used by the uniform batch create endpoint.
    /// </summary>
    public class BatchModelEndpointRequest
    {
        #region Public-Members

        /// <summary>
        /// The model endpoints to create.
        /// </summary>
        public List<ModelEndpoint> Items { get; set; } = new List<ModelEndpoint>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a batch model endpoint request.
        /// </summary>
        public BatchModelEndpointRequest()
        {
        }

        #endregion
    }
}
