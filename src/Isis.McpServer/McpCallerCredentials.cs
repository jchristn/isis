namespace Isis.McpServer
{
    /// <summary>
    /// The credentials a caller presented on the MCP transport, forwarded to the Isis REST API so that the
    /// REST server performs the authoritative authentication and tenant scoping.
    /// </summary>
    public class McpCallerCredentials
    {
        #region Public-Members

        /// <summary>
        /// The tenant credential access key presented in the x-access-key header, if any.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// The tenant credential secret key presented in the x-secret-key header, if any.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a complete credential (access key and secret key) is present.
        /// </summary>
        /// <returns>True when both the access key and secret key were supplied.</returns>
        public bool HasAny()
        {
            return !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);
        }

        #endregion
    }
}
