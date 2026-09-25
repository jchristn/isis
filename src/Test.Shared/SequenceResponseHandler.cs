namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An HTTP message handler that answers each request with the next status and body from a list, repeating the last
    /// one, for retry tests. Records every request body it received.
    /// </summary>
    internal sealed class SequenceResponseHandler : HttpMessageHandler
    {
        #region Internal-Members

        /// <summary>
        /// Bodies of the requests received, in order.
        /// </summary>
        internal List<string> Bodies { get; } = new List<string>();

        #endregion

        #region Private-Members

        private readonly List<KeyValuePair<HttpStatusCode, string>> _Responses;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the responses to return, in order.
        /// </summary>
        /// <param name="responses">Status and body pairs.</param>
        internal SequenceResponseHandler(List<KeyValuePair<HttpStatusCode, string>> responses)
        {
            _Responses = responses;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : string.Empty);
            KeyValuePair<HttpStatusCode, string> next = _Responses[System.Math.Min(Bodies.Count - 1, _Responses.Count - 1)];
            HttpResponseMessage response = new HttpResponseMessage(next.Key);
            response.Content = new StringContent(next.Value, Encoding.UTF8, "application/json");
            return response;
        }

        #endregion
    }
}
