namespace Isis.Core.Models
{
    using System.Collections.Generic;
    using Isis.Core.Enums;

    /// <summary>
    /// The result of a chat-with-memory turn: a synthesized answer grounded in retrieved memories.
    /// </summary>
    public class ChatAnswer
    {
        #region Public-Members

        /// <summary>
        /// The synthesized answer.
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// The memories that grounded the answer.
        /// </summary>
        public List<ChatCitation> Citations { get; set; } = new List<ChatCitation>();

        /// <summary>
        /// The retrieval strategy actually used.
        /// </summary>
        public SearchModeEnum RetrievalMode { get; set; } = SearchModeEnum.Keyword;

        /// <summary>
        /// An optional notice, for example when retrieval degraded or no memories were found.
        /// </summary>
        public string? Notice { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a chat answer.
        /// </summary>
        public ChatAnswer()
        {
        }

        #endregion
    }
}
