namespace Isis.Core.Models
{
    /// <summary>
    /// A reference to a memory that grounded a chat answer.
    /// </summary>
    public class ChatCitation
    {
        #region Public-Members

        /// <summary>
        /// The memory slug.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// The memory title, when known.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// The relevance score of the cited memory.
        /// </summary>
        public double Score { get; set; } = 0.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a chat citation.
        /// </summary>
        public ChatCitation()
        {
        }

        #endregion
    }
}
