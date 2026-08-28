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
        /// The tenant credential access key. Presented either as an <c>Authorization: Bearer &lt;accessKey&gt;</c>
        /// token (the form MCP clients such as Mux support) or in the <c>x-access-key</c> header. The access key
        /// is the public, transferable material and is sufficient on its own to authenticate an MCP caller.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// The tenant credential secret key, if the client supplied it in the <c>x-secret-key</c> header. The
        /// secret is never required by the MCP server; clients that cannot send a second header (e.g. Mux)
        /// keep the secret entirely client-side. When present it is validated by the REST server.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the caller presented an access key. The access key alone is sufficient to authenticate;
        /// the secret key is optional.
        /// </summary>
        /// <returns>True when an access key was supplied.</returns>
        public bool HasAny()
        {
            return !string.IsNullOrEmpty(AccessKey);
        }

        #endregion
    }
}
