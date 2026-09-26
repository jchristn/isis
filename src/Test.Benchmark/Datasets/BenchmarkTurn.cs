namespace Test.Benchmark.Datasets
{
    /// <summary>
    /// An earlier message in a conversation, sent with a follow-up question.
    /// </summary>
    public class BenchmarkTurn
    {
        #region Public-Members

        /// <summary>
        /// "user" or "assistant".
        /// </summary>
        public string Role { get; set; } = "user";

        /// <summary>
        /// The message text.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        #endregion
    }
}
