namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;

    /// <summary>
    /// A suite of agent tasks run against one ingested dataset corpus.
    /// </summary>
    public class AgentTaskFile
    {
        #region Public-Members

        /// <summary>
        /// Suite name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Path (relative to the task file) of the dataset whose first corpus is the agent's memory.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// The tasks.
        /// </summary>
        public List<AgentTask> Tasks { get; set; } = new List<AgentTask>();

        #endregion
    }
}
