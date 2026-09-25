namespace Isis.Core.Recall
{
    using System;

    /// <summary>
    /// A model endpoint stayed temporarily unavailable (429, 502, or 503) after retries. Callers should report it as a
    /// retryable 503 rather than as a problem with the request. Derives from <see cref="InvalidOperationException"/> so
    /// existing handlers of endpoint failures still catch it.
    /// </summary>
    public class ModelEndpointUnavailableException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// The HTTP status the endpoint last returned.
        /// </summary>
        public int StatusCode { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="statusCode">The HTTP status the endpoint last returned.</param>
        public ModelEndpointUnavailableException(string message, int statusCode)
            : base(message)
        {
            StatusCode = statusCode;
        }

        #endregion
    }
}
