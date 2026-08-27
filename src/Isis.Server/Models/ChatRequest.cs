namespace Isis.Server.Models
{
    /// <summary>
    /// A chat-with-memory request body.
    /// </summary>
    public class ChatRequest
    {
        #region Public-Members

        /// <summary>
        /// The natural-language question.
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// The inference endpoint to use. When null, the tenant's first active inference endpoint is used.
        /// </summary>
        public string? InferenceEndpointId { get; set; } = null;

        /// <summary>
        /// The maximum number of memories to retrieve. Default 5.
        /// </summary>
        public int TopK { get; set; } = 5;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a chat request.
        /// </summary>
        public ChatRequest()
        {
        }

        #endregion
    }
}
