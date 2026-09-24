namespace Test.Benchmark
{
    /// <summary>
    /// Raw outcome of one timed HTTP call.
    /// </summary>
    internal class TimedCall
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code (0 when the request failed before a response).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// True for a 2xx response.
        /// </summary>
        public bool IsSuccess { get; set; } = false;

        /// <summary>
        /// Response body text (or the transport error message).
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Client-observed elapsed time in milliseconds.
        /// </summary>
        public double ElapsedMs { get; set; } = 0.0;

        #endregion
    }
}
