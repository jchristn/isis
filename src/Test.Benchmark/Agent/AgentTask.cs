namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;

    /// <summary>
    /// One agent task: a prompt whose correct answer depends on the project's memory, graded by regexes.
    /// </summary>
    public class AgentTask
    {
        #region Public-Members

        /// <summary>
        /// Task id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Task type (for breakdowns).
        /// </summary>
        public string Type { get; set; } = "recall";

        /// <summary>
        /// The prompt given to the agent.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Case-insensitive regexes that must ALL match the agent's final answer.
        /// </summary>
        public List<string> Expect { get; set; } = new List<string>();

        /// <summary>
        /// Case-insensitive regexes that must NOT match (for example a superseded or wrong value).
        /// </summary>
        public List<string> Forbid { get; set; } = new List<string>();

        #endregion
    }
}
