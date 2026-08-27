namespace Isis.Core.Enums
{
    /// <summary>
    /// The role of a message within a chat-with-memory conversation.
    /// </summary>
    public enum ChatRoleEnum
    {
        /// <summary>
        /// A system/grounding instruction.
        /// </summary>
        System,

        /// <summary>
        /// A question or statement from the human user.
        /// </summary>
        User,

        /// <summary>
        /// A synthesized answer produced from retrieved memories.
        /// </summary>
        Assistant
    }
}
