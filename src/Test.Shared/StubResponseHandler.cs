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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new HttpResponseMessage(_Status);
            response.Content = new StringContent(_Body, Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }

        #endregion
    }
}
