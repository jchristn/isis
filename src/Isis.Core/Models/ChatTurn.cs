namespace Isis.Core.Models
{
    /// <summary>
    /// One earlier message in a chat conversation, sent by the caller so a follow-up question can be understood.
    /// </summary>
    public class ChatTurn
    {
        #region Public-Members

        /// <summary>
        /// Who sent the message: "user" or "assistant". Any other value is treated as "user".
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// The message text.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a chat turn.
        /// </summary>
        public ChatTurn()
        {
        }

        #endregion
    }
}
