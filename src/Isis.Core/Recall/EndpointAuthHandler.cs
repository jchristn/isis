namespace Isis.Core.Recall
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Models;

    /// <summary>
    /// A delegating handler that applies a model endpoint's configured authentication to every outbound
    /// request. Used to wrap the transport that PolyPrompt clients call through, so Isis's generic auth
    /// (bearer / custom header / query param / basic / access-secret) is applied uniformly even though
    /// PolyPrompt itself has no generic auth configuration.
    /// </summary>
    public class EndpointAuthHandler : DelegatingHandler
    {
        #region Private-Members

        private readonly ModelEndpoint _Endpoint;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the handler for a given endpoint over an inner transport.
        /// </summary>
        /// <param name="endpoint">The endpoint whose auth to apply.</param>
        /// <param name="innerHandler">The inner transport handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public EndpointAuthHandler(ModelEndpoint endpoint, HttpMessageHandler innerHandler)
            : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            EndpointAuthenticator.Apply(request, _Endpoint);
            return base.SendAsync(request, cancellationToken);
        }

        #endregion
    }
}
