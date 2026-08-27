namespace Isis.Core.Responses
{
    using System;

    /// <summary>
    /// A standard error response body.
    /// </summary>
    public class ErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// A short machine-readable error code.
        /// </summary>
        public string Error { get; set; } = "Error";

        /// <summary>
        /// A human-readable error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty error response.
        /// </summary>
        public ErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate an error response.
        /// </summary>
        /// <param name="error">The error code.</param>
        /// <param name="message">The error message.</param>
        public ErrorResponse(string error, string message)
        {
            if (String.IsNullOrEmpty(error)) throw new ArgumentNullException(nameof(error));
            Error = error;
            Message = message ?? string.Empty;
        }

        #endregion
    }
}
