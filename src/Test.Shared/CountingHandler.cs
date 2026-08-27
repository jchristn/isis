namespace Test.Shared
{
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An HTTP message handler that counts requests and returns a fixed status, for health-check tests.
    /// </summary>
    internal sealed class CountingHandler : HttpMessageHandler
    {
        #region Private-Members

        private int _Count = 0;
        private readonly HttpStatusCode _Status;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a counting handler.
        /// </summary>
        /// <param name="status">The status code to return.</param>
        internal CountingHandler(HttpStatusCode status = HttpStatusCode.OK)
        {
            _Status = status;
        }

        #endregion

        #region Internal-Members

        /// <summary>
        /// The number of requests handled.
        /// </summary>
        internal int Count
        {
            get
            {
                return Volatile.Read(ref _Count);
            }
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Reset the request counter.
        /// </summary>
        internal void Reset()
        {
            Interlocked.Exchange(ref _Count, 0);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _Count);
            return Task.FromResult(new HttpResponseMessage(_Status));
        }

        #endregion
    }
}
