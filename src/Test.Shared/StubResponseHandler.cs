namespace Test.Shared
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An HTTP message handler that returns a fixed JSON body and status, for inference/embedding tests.
    /// </summary>
    internal sealed class StubResponseHandler : HttpMessageHandler
    {
        #region Internal-Members

        /// <summary>
        /// The body of the most recent request, so tests can assert on what was sent (for example a chat prompt).
        /// </summary>
        internal string? LastRequestBody { get; private set; } = null;

        /// <summary>
        /// The URI of the most recent request.
        /// </summary>
        internal System.Uri? LastRequestUri { get; private set; } = null;

        /// <summary>
        /// The number of requests handled.
        /// </summary>
        internal int RequestCount { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly string _Body;
        private readonly HttpStatusCode _Status;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a stub response handler.
        /// </summary>
        /// <param name="body">The JSON body to return.</param>
        /// <param name="status">The status code to return.</param>
        internal StubResponseHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _Body = body;
            _Status = status;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            if (request.Content != null) LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            HttpResponseMessage response = new HttpResponseMessage(_Status);
            response.Content = new StringContent(_Body, Encoding.UTF8, "application/json");
            return response;
        }

        #endregion
    }
}
