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

        /// <summary>
        /// The inference model that produced the answer.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Prompt (input) token count reported by the endpoint, when available.
        /// </summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion (output) token count reported by the endpoint, when available.
        /// </summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Total token count (prompt + completion).
        /// </summary>
        public int TotalTokens { get; set; } = 0;

        /// <summary>
        /// Milliseconds from request start to the first streamed answer token (time to first token).
        /// </summary>
        public double TimeToFirstTokenMs { get; set; } = 0.0;

        /// <summary>
        /// Milliseconds spent generating the answer (first token to last).
        /// </summary>
        public double GenerationMs { get; set; } = 0.0;

        /// <summary>
        /// Output tokens per second over the generation window, when token counts are available.
        /// </summary>
        public double TokensPerSecond { get; set; } = 0.0;

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
