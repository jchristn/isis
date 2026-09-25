namespace Isis.Core.Recall
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A delegating handler that retries a model endpoint request when the endpoint is temporarily unavailable:
    /// 429 (too many requests), 502 (bad gateway, for example a router with no healthy backend), and 503 (service
    /// unavailable). It waits with exponential backoff and jitter, or for the endpoint's Retry-After when it sends one.
    /// Model calls carry no side effects, so resending is safe. Other failures are returned unchanged.
    /// </summary>
    public class TransientRetryHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>
        /// Maximum number of retries after the first attempt. Default 3, minimum 0, maximum 10.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set outside [0, 10].</exception>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                if (value < 0 || value > 10) throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries must be between 0 and 10.");
                _MaxRetries = value;
            }
        }

        /// <summary>
        /// Delay before the first retry; each later retry doubles it. Default 500 ms, minimum 1 ms.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1 ms.</exception>
        public TimeSpan BaseDelay
        {
            get
            {
                return _BaseDelay;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(1)) throw new ArgumentOutOfRangeException(nameof(BaseDelay), "BaseDelay must be at least 1 ms.");
                _BaseDelay = value;
            }
        }

        /// <summary>
        /// Longest single wait, including a Retry-After sent by the endpoint. Default 10 seconds, minimum 1 ms.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 1 ms.</exception>
        public TimeSpan MaxDelay
        {
            get
            {
                return _MaxDelay;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(1)) throw new ArgumentOutOfRangeException(nameof(MaxDelay), "MaxDelay must be at least 1 ms.");
                _MaxDelay = value;
            }
        }

        #endregion

        #region Private-Members

        private int _MaxRetries = 3;
        private TimeSpan _BaseDelay = TimeSpan.FromMilliseconds(500);
        private TimeSpan _MaxDelay = TimeSpan.FromSeconds(10);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the handler over an inner transport.
        /// </summary>
        /// <param name="innerHandler">The inner transport handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when innerHandler is null.</exception>
        public TransientRetryHandler(HttpMessageHandler innerHandler)
            : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a status code means the endpoint is temporarily unavailable and the request should be retried.
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        /// <returns>True for 429, 502, and 503.</returns>
        public static bool IsTransient(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.BadGateway || statusCode == HttpStatusCode.ServiceUnavailable;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Buffer the body once so every attempt can resend it.
            if (request.Content != null) await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!IsTransient(response.StatusCode) || attempt >= _MaxRetries) return response;

                TimeSpan delay = RetryDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
        {
            TimeSpan? retryAfter = null;
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue) retryAfter = response.Headers.RetryAfter.Delta.Value;
                else if (response.Headers.RetryAfter.Date.HasValue) retryAfter = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            }

            double backoffMs = _BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
            double jitterMs = Random.Shared.NextDouble() * _BaseDelay.TotalMilliseconds;
            TimeSpan delay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter.Value : TimeSpan.FromMilliseconds(backoffMs + jitterMs);
            return delay > _MaxDelay ? _MaxDelay : delay;
        }

        #endregion
    }
}
