namespace Test.Benchmark
{
    /// <summary>
    /// Outcome of a timed call whose body is not needed.
    /// </summary>
    public class TimedResponse
    {
        #region Public-Members

        /// <summary>
        /// HTTP status code (0 when the request failed before a response).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Client-observed elapsed time in milliseconds.
        /// </summary>
        public double ElapsedMs { get; set; } = 0.0;

        /// <summary>
        /// Error text for a failed call, else null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// True for a 2xx response.
        /// </summary>
        public bool IsSuccess
        {
            get
            {
                return StatusCode >= 200 && StatusCode < 300;
            }
        }

        #endregion
    }
}
