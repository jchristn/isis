namespace Isis.Server.Models
{
    using System.Collections.Generic;
    using Isis.Core.Models;

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
        /// The maximum number of memories to retrieve. 0 (the default) uses the server's default, which is 8 unless
        /// configured otherwise (see <c>MemoryChatService.DefaultTopK</c>).
        /// </summary>
        public int TopK { get; set; } = 0;

        /// <summary>
        /// Earlier messages in the conversation, oldest first, each with a role ("user" or "assistant") and content.
        /// When present, a follow-up question is rewritten into a standalone query for retrieval and the answer prompt
        /// shows the recent conversation. Only the most recent messages are used (server setting
        /// <c>retrieval.chatHistoryTurns</c>, default 6). Null or empty for a single question.
        /// </summary>
        public List<ChatTurn>? History { get; set; } = null;

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
